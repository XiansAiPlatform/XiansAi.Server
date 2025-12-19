using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models.Usage;
using Shared.Utils;

namespace Shared.Repositories;

public interface IUsageEventRepository
{
    Task InsertAsync(UsageEvent usageEvent, CancellationToken cancellationToken = default);
    Task<List<UsageEvent>> GetEventsAsync(string tenantId, string? userId, DateTime? since = null, CancellationToken cancellationToken = default);
    
    // Generic aggregation method for usage events statistics
    Task<UsageEventsResponse> GetUsageEventsAsync(
        string tenantId, 
        string? userId,  // null or "all" = all users, specific userId = filtered
        string? agentName,  // null or "all" = all agents, specific agentName = filtered
        UsageType type,
        DateTime startDate, 
        DateTime endDate, 
        string groupBy = "day",  // "day" | "week" | "month"
        CancellationToken cancellationToken = default);

    Task<List<UserListItem>> GetUsersWithUsageAsync(
        string tenantId, 
        CancellationToken cancellationToken = default);
}

public class UsageEventRepository : IUsageEventRepository
{
    private readonly IMongoCollection<UsageEvent> _collection;
    private readonly ILogger<UsageEventRepository> _logger;

    public UsageEventRepository(IDatabaseService databaseService, ILogger<UsageEventRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<UsageEvent>("usage_events");
        _logger = logger;
    }

    public async Task InsertAsync(UsageEvent usageEvent, CancellationToken cancellationToken = default)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            usageEvent.CreatedAt = usageEvent.CreatedAt == default ? DateTime.UtcNow : usageEvent.CreatedAt;
            await _collection.InsertOneAsync(usageEvent, cancellationToken: cancellationToken);
        }, _logger, operationName: "InsertUsageEvent");
    }

    public async Task<List<UsageEvent>> GetEventsAsync(string tenantId, string? userId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var builder = Builders<UsageEvent>.Filter;
            var filters = new List<FilterDefinition<UsageEvent>>
            {
                builder.Eq(x => x.TenantId, tenantId)
            };

            if (!string.IsNullOrWhiteSpace(userId))
            {
                filters.Add(builder.Eq(x => x.UserId, userId));
            }

            if (since.HasValue)
            {
                filters.Add(builder.Gt(x => x.CreatedAt, since.Value));
            }

            var filter = builder.And(filters);
            return await _collection.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .Limit(1000)
                .ToListAsync(cancellationToken);
        }, _logger, operationName: "GetUsageEvents");
    }

    public async Task<UsageEventsResponse> GetUsageEventsAsync(
        string tenantId, 
        string? userId,
        string? agentName,
        UsageType type,
        DateTime startDate, 
        DateTime endDate, 
        string groupBy = "day", 
        CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Build base match filter conditions
            var baseConditions = new BsonDocument
            {
                { "tenant_id", tenantId },
                { "created_at", new BsonDocument
                    {
                        { "$gte", startDate },
                        { "$lte", endDate }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(userId) && userId != "all")
            {
                baseConditions.Add("user_id", userId);
            }

            // Build final match filter - use $and if we need agent filtering with $expr
            BsonDocument matchFilter;
            if (!string.IsNullOrWhiteSpace(agentName) && agentName != "all")
            {
                // Trim the agent name to handle leading/trailing spaces
                var trimmedAgentName = agentName.Trim();
                
                // Use $expr to extract agent name from workflow_id and compare
                // We need $and to combine regular field matches with $expr
                var agentNameCondition = new BsonDocument("$expr", new BsonDocument
                {
                    { "$eq", new BsonArray
                        {
                            new BsonDocument("$trim", new BsonDocument
                            {
                                { "input", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                }
                            }),
                            trimmedAgentName
                        }
                    }
                });
                
                matchFilter = new BsonDocument("$and", new BsonArray
                {
                    baseConditions,
                    agentNameCondition
                });
            }
            else
            {
                matchFilter = baseConditions;
            }

            // Build aggregation pipelines based on usage type
            var (totalPipeline, timeSeriesPipeline, userBreakdownPipeline, agentBreakdownPipeline, agentTimeSeriesPipeline, sortField) = type switch
            {
                UsageType.Tokens => BuildTokenPipelines(matchFilter, groupBy),
                UsageType.Messages => BuildMessagePipelines(matchFilter, groupBy),
                UsageType.ResponseTime => BuildResponseTimePipelines(matchFilter, groupBy),
                _ => throw new ArgumentException($"Unsupported usage type: {type}")
            };

            // Get totals
            var totalResult = await _collection.Aggregate<BsonDocument>(totalPipeline, cancellationToken: cancellationToken).FirstOrDefaultAsync(cancellationToken);
            var totalMetrics = ParseTotalMetrics(totalResult, type);

            // Get time series data
            var timeSeriesResult = await _collection.Aggregate<BsonDocument>(timeSeriesPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var timeSeriesData = timeSeriesResult.Select(doc => new TimeSeriesDataPoint
            {
                Date = ParseDateFromGrouping(doc["_id"].AsString, groupBy),
                Metrics = ParseTimeSeriesMetrics(doc, type)
            }).ToList();

            // Get user breakdown
            var userBreakdownResult = await _collection.Aggregate<BsonDocument>(userBreakdownPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var userBreakdown = userBreakdownResult.Select(doc => new UserBreakdown
            {
                UserId = doc["_id"].AsString,
                UserName = doc["_id"].AsString, // Use user ID as name for MVP
                Metrics = ParseUserMetrics(doc, type)
            }).ToList();

            // Get agent breakdown
            var agentBreakdownResult = await _collection.Aggregate<BsonDocument>(agentBreakdownPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var agentBreakdown = agentBreakdownResult.Select(doc => new AgentBreakdown
            {
                AgentName = doc["_id"].AsString ?? "Unknown",
                Metrics = ParseAgentMetrics(doc, type)
            }).ToList();

            // Get agent time series data (grouped by date and agent)
            var agentTimeSeriesResult = await _collection.Aggregate<BsonDocument>(agentTimeSeriesPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var agentTimeSeriesData = agentTimeSeriesResult.Select(doc => new AgentTimeSeriesDataPoint
            {
                Date = ParseDateFromGrouping(doc["date"].AsString, groupBy),
                AgentName = doc["agent"].AsString ?? "Unknown",
                Metrics = ParseAgentTimeSeriesMetrics(doc, type)
            }).ToList();

            return new UsageEventsResponse
            {
                TenantId = tenantId,
                UserId = string.IsNullOrWhiteSpace(userId) || userId == "all" ? null : userId,
                Type = type,
                StartDate = startDate,
                EndDate = endDate,
                TotalMetrics = totalMetrics,
                TimeSeriesData = timeSeriesData,
                UserBreakdown = userBreakdown,
                AgentBreakdown = agentBreakdown,
                AgentTimeSeriesData = agentTimeSeriesData
            };
        }, _logger, operationName: $"GetUsageEvents_{type}");
    }

    /// <summary>
    /// Extracts agent name from workflow_id.
    /// Format: tenant:AgentName:FlowName:optionalId
    /// Returns the agent name (second part after splitting by ':')
    /// </summary>
    private static string ExtractAgentName(string? workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return "Unknown";

        var parts = workflowId.Split(':');
        if (parts.Length >= 2)
            return parts[1]; // Agent name is the second part
        
        return "Unknown";
    }

    private (BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], string) BuildTokenPipelines(BsonDocument matchFilter, string groupBy)
    {
        var dateFormat = GetDateFormat(groupBy);

        var totalPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "primaryCount", new BsonDocument("$sum", "$total_tokens") },
                { "promptCount", new BsonDocument("$sum", "$prompt_tokens") },
                { "completionCount", new BsonDocument("$sum", "$completion_tokens") },
                { "requestCount", new BsonDocument("$sum", 1) }
            })
        };

        var timeSeriesPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$dateToString", new BsonDocument
                    {
                        { "format", dateFormat },
                        { "date", "$created_at" }
                    })
                },
                { "primaryCount", new BsonDocument("$sum", "$total_tokens") },
                { "promptCount", new BsonDocument("$sum", "$prompt_tokens") },
                { "completionCount", new BsonDocument("$sum", "$completion_tokens") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        var userBreakdownPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$user_id" },
                { "primaryCount", new BsonDocument("$sum", "$total_tokens") },
                { "promptCount", new BsonDocument("$sum", "$prompt_tokens") },
                { "completionCount", new BsonDocument("$sum", "$completion_tokens") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("primaryCount", -1)),
            new BsonDocument("$limit", 100)
        };

        var agentBreakdownPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$addFields", new BsonDocument
            {
                { "agent_name", new BsonDocument("$trim", new BsonDocument
                    {
                        { "input", new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                2
                            })
                        }
                    })
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) },
                { "primaryCount", new BsonDocument("$sum", "$total_tokens") },
                { "promptCount", new BsonDocument("$sum", "$prompt_tokens") },
                { "completionCount", new BsonDocument("$sum", "$completion_tokens") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("primaryCount", -1)),
            new BsonDocument("$limit", 100)
        };

        var agentTimeSeriesPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$addFields", new BsonDocument
            {
                { "agent_name", new BsonDocument("$trim", new BsonDocument
                    {
                        { "input", new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                2
                            })
                        }
                    })
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "date", new BsonDocument("$dateToString", new BsonDocument
                            {
                                { "format", dateFormat },
                                { "date", "$created_at" }
                            })
                        },
                        { "agent", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) }
                    }
                },
                { "primaryCount", new BsonDocument("$sum", "$total_tokens") },
                { "promptCount", new BsonDocument("$sum", "$prompt_tokens") },
                { "completionCount", new BsonDocument("$sum", "$completion_tokens") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "date", "$_id.date" },
                { "agent", "$_id.agent" },
                { "primaryCount", 1 },
                { "promptCount", 1 },
                { "completionCount", 1 },
                { "requestCount", 1 }
            }),
            new BsonDocument("$sort", new BsonDocument("date", 1))
        };

        return (totalPipeline, timeSeriesPipeline, userBreakdownPipeline, agentBreakdownPipeline, agentTimeSeriesPipeline, "primaryCount");
    }

    private (BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], string) BuildMessagePipelines(BsonDocument matchFilter, string groupBy)
    {
        var dateFormat = GetDateFormat(groupBy);

        var totalPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "primaryCount", new BsonDocument("$sum", "$message_count") },
                { "requestCount", new BsonDocument("$sum", 1) }
            })
        };

        var timeSeriesPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$dateToString", new BsonDocument
                    {
                        { "format", dateFormat },
                        { "date", "$created_at" }
                    })
                },
                { "primaryCount", new BsonDocument("$sum", "$message_count") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        var userBreakdownPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$user_id" },
                { "primaryCount", new BsonDocument("$sum", "$message_count") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("primaryCount", -1)),
            new BsonDocument("$limit", 100)
        };

        var agentBreakdownPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$addFields", new BsonDocument
            {
                { "agent_name", new BsonDocument("$trim", new BsonDocument
                    {
                        { "input", new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                2
                            })
                        }
                    })
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) },
                { "primaryCount", new BsonDocument("$sum", "$message_count") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("primaryCount", -1)),
            new BsonDocument("$limit", 100)
        };

        var agentTimeSeriesPipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$addFields", new BsonDocument
            {
                { "agent_name", new BsonDocument("$trim", new BsonDocument
                    {
                        { "input", new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                2
                            })
                        }
                    })
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "date", new BsonDocument("$dateToString", new BsonDocument
                            {
                                { "format", dateFormat },
                                { "date", "$created_at" }
                            })
                        },
                        { "agent", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) }
                    }
                },
                { "primaryCount", new BsonDocument("$sum", "$message_count") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "date", "$_id.date" },
                { "agent", "$_id.agent" },
                { "primaryCount", 1 },
                { "requestCount", 1 }
            }),
            new BsonDocument("$sort", new BsonDocument("date", 1))
        };

        return (totalPipeline, timeSeriesPipeline, userBreakdownPipeline, agentBreakdownPipeline, agentTimeSeriesPipeline, "primaryCount");
    }

    private (BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], string) BuildResponseTimePipelines(BsonDocument matchFilter, string groupBy)
    {
        var dateFormat = GetDateFormat(groupBy);

        // Filter out documents where response_time_ms is null
        var matchWithResponseTime = matchFilter.DeepClone().AsBsonDocument;
        matchWithResponseTime.Add("response_time_ms", new BsonDocument("$ne", BsonNull.Value));

        var totalPipeline = new[]
        {
            new BsonDocument("$match", matchWithResponseTime),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "primaryCount", new BsonDocument("$sum", "$response_time_ms") },
                { "requestCount", new BsonDocument("$sum", 1) }
            })
        };

        var timeSeriesPipeline = new[]
        {
            new BsonDocument("$match", matchWithResponseTime),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$dateToString", new BsonDocument
                    {
                        { "format", dateFormat },
                        { "date", "$created_at" }
                    })
                },
                { "primaryCount", new BsonDocument("$sum", "$response_time_ms") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        var userBreakdownPipeline = new[]
        {
            new BsonDocument("$match", matchWithResponseTime),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$user_id" },
                { "primaryCount", new BsonDocument("$sum", "$response_time_ms") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("primaryCount", -1)),
            new BsonDocument("$limit", 100)
        };

        var agentBreakdownPipeline = new[]
        {
            new BsonDocument("$match", matchWithResponseTime),
            new BsonDocument("$addFields", new BsonDocument
            {
                { "agent_name", new BsonDocument("$trim", new BsonDocument
                    {
                        { "input", new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                2
                            })
                        }
                    })
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) },
                { "primaryCount", new BsonDocument("$sum", "$response_time_ms") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("primaryCount", -1)),
            new BsonDocument("$limit", 100)
        };

        var agentTimeSeriesPipeline = new[]
        {
            new BsonDocument("$match", matchWithResponseTime),
            new BsonDocument("$addFields", new BsonDocument
            {
                { "agent_name", new BsonDocument("$trim", new BsonDocument
                    {
                        { "input", new BsonDocument("$arrayElemAt", new BsonArray
                            {
                                new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                2
                            })
                        }
                    })
                }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "date", new BsonDocument("$dateToString", new BsonDocument
                            {
                                { "format", dateFormat },
                                { "date", "$created_at" }
                            })
                        },
                        { "agent", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) }
                    }
                },
                { "primaryCount", new BsonDocument("$sum", "$response_time_ms") },
                { "requestCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "date", "$_id.date" },
                { "agent", "$_id.agent" },
                { "primaryCount", 1 },
                { "requestCount", 1 }
            }),
            new BsonDocument("$sort", new BsonDocument("date", 1))
        };

        return (totalPipeline, timeSeriesPipeline, userBreakdownPipeline, agentBreakdownPipeline, agentTimeSeriesPipeline, "primaryCount");
    }

    private static string GetDateFormat(string groupBy) => groupBy switch
    {
        "hour" => "%Y-%m-%dT%H:00:00",  // Hour: 2025-12-08T14:00:00
        "week" => "%Y-%U",               // Week: 2025-49 (Sunday-based)
        "month" => "%Y-%m",              // Month: 2025-12
        _ => "%Y-%m-%d"                  // Day: 2025-12-08 (default)
    };

    /// <summary>
    /// Safely converts BsonValue to long, handling both BsonInt32 and BsonInt64
    /// </summary>
    private static long ToInt64(BsonValue value)
    {
        if (value.IsBsonNull)
            return 0;
        
        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => (long)value.AsDouble,
            _ => 0
        };
    }

    /// <summary>
    /// Safely converts BsonValue to int, handling both BsonInt32 and BsonInt64
    /// </summary>
    private static int ToInt32(BsonValue value)
    {
        if (value.IsBsonNull)
            return 0;
        
        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => (int)value.AsInt64,
            BsonType.Double => (int)value.AsDouble,
            _ => 0
        };
    }

    private static UsageMetrics ParseTotalMetrics(BsonDocument? doc, UsageType type)
    {
        if (doc == null)
        {
            return new UsageMetrics
            {
                PrimaryCount = 0,
                RequestCount = 0
            };
        }

        return type switch
        {
            UsageType.Tokens => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"]),
                PromptCount = ToInt64(doc["promptCount"]),
                CompletionCount = ToInt64(doc["completionCount"])
            },
            UsageType.Messages => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            UsageType.ResponseTime => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            _ => throw new ArgumentException($"Unsupported usage type: {type}")
        };
    }

    private static UsageMetrics ParseTimeSeriesMetrics(BsonDocument doc, UsageType type)
    {
        return type switch
        {
            UsageType.Tokens => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"]),
                PromptCount = ToInt64(doc["promptCount"]),
                CompletionCount = ToInt64(doc["completionCount"])
            },
            UsageType.Messages => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            UsageType.ResponseTime => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            _ => throw new ArgumentException($"Unsupported usage type: {type}")
        };
    }

    private static UsageMetrics ParseUserMetrics(BsonDocument doc, UsageType type)
    {
        return type switch
        {
            UsageType.Tokens => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"]),
                PromptCount = ToInt64(doc["promptCount"]),
                CompletionCount = ToInt64(doc["completionCount"])
            },
            UsageType.Messages => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            UsageType.ResponseTime => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            _ => throw new ArgumentException($"Unsupported usage type: {type}")
        };
    }

    private static UsageMetrics ParseAgentMetrics(BsonDocument doc, UsageType type)
    {
        return type switch
        {
            UsageType.Tokens => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"]),
                PromptCount = ToInt64(doc["promptCount"]),
                CompletionCount = ToInt64(doc["completionCount"])
            },
            UsageType.Messages => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            UsageType.ResponseTime => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            _ => throw new ArgumentException($"Unsupported usage type: {type}")
        };
    }

    private static UsageMetrics ParseAgentTimeSeriesMetrics(BsonDocument doc, UsageType type)
    {
        return type switch
        {
            UsageType.Tokens => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"]),
                PromptCount = ToInt64(doc["promptCount"]),
                CompletionCount = ToInt64(doc["completionCount"])
            },
            UsageType.Messages => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            UsageType.ResponseTime => new UsageMetrics
            {
                PrimaryCount = ToInt64(doc["primaryCount"]),
                RequestCount = ToInt32(doc["requestCount"])
            },
            _ => throw new ArgumentException($"Unsupported usage type: {type}")
        };
    }

    public async Task<List<UserListItem>> GetUsersWithUsageAsync(
        string tenantId, 
        CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var pipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument("tenant_id", tenantId)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$user_id" }
                }),
                new BsonDocument("$sort", new BsonDocument("_id", 1))
            };

            var result = await _collection.Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            
            return result.Select(doc => new UserListItem
            {
                UserId = doc["_id"].AsString,
                UserName = doc["_id"].AsString, // Use user ID as name for MVP
                Email = null
            }).ToList();
        }, _logger, operationName: "GetUsersWithUsage");
    }

    private static DateTime ParseDateFromGrouping(string dateString, string groupBy)
    {
        return groupBy switch
        {
            "hour" => DateTime.Parse(dateString, null, System.Globalization.DateTimeStyles.RoundtripKind),
            "week" => ParseWeekDate(dateString),
            "month" => DateTime.ParseExact(dateString, "yyyy-MM", null, System.Globalization.DateTimeStyles.None),
            _ => DateTime.ParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None)
        };
    }

    private static DateTime ParseWeekDate(string weekString)
    {
        // Format: "2025-00" to "2025-53" (year-week from %U format)
        var parts = weekString.Split('-');
        var year = int.Parse(parts[0]);
        var week = int.Parse(parts[1]);
        
        // Calculate the first day of the year
        var jan1 = new DateTime(year, 1, 1);
        
        // Find the first Sunday of the year
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7;
        var firstSunday = jan1.AddDays(daysUntilSunday);
        
        // Add weeks
        return firstSunday.AddDays(week * 7);
    }
}

