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
    
    Task<int> DeleteByAgentAsync(string tenantId, string agentName, CancellationToken cancellationToken = default);
    
    // Flexible aggregation method for usage events statistics
    Task<UsageEventsResponse> GetUsageEventsAsync(
        string tenantId, 
        string? userId,  // null or "all" = all users, specific userId = filtered
        string? agentName,  // null or "all" = all agents, specific agentName = filtered
        string? category,  // metric category filter
        string? metricType,  // specific metric type
        DateTime startDate, 
        DateTime endDate, 
        string groupBy = "day",  // "day" | "week" | "month"
        CancellationToken cancellationToken = default);

    Task<List<UserListItem>> GetUsersWithUsageAsync(
        string tenantId, 
        CancellationToken cancellationToken = default);
    
    // Get available metrics for discovery
    Task<AvailableMetricsResponse> GetAvailableMetricsAsync(
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

    public async Task<int> DeleteByAgentAsync(string tenantId, string agentName, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var agentRegex = new BsonRegularExpression($":{Regex.Escape(agentName)}:", "i");
            var tenantFilter = string.IsNullOrEmpty(tenantId)
                ? Builders<UsageEvent>.Filter.Or(
                    Builders<UsageEvent>.Filter.Eq(x => x.TenantId, null),
                    Builders<UsageEvent>.Filter.Eq(x => x.TenantId, string.Empty))
                : Builders<UsageEvent>.Filter.Eq(x => x.TenantId, tenantId);

            var filter = Builders<UsageEvent>.Filter.And(tenantFilter, Builders<UsageEvent>.Filter.Regex(x => x.WorkflowId, agentRegex));

            var result = await _collection.DeleteManyAsync(filter, cancellationToken);
            return (int)result.DeletedCount;
        }, _logger, operationName: "DeleteUsageEventsByAgent");
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
        string? category,
        string? metricType,
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

            // Add agent_name filter if specified
            if (!string.IsNullOrWhiteSpace(agentName) && agentName != "all")
            {
                baseConditions.Add("agent_name", agentName.Trim());
            }

            // Build pipeline with $unwind for metrics array
            var dateFormat = GetDateFormat(groupBy);
            
            // Metric filter conditions
            var metricMatchConditions = new BsonDocument();
            if (!string.IsNullOrWhiteSpace(category) && category != "all")
            {
                metricMatchConditions.Add("metrics.category", category);
            }
            if (!string.IsNullOrWhiteSpace(metricType))
            {
                metricMatchConditions.Add("metrics.type", metricType);
            }

            // Build pipelines for flexible metrics
            var (totalPipeline, timeSeriesPipeline, userBreakdownPipeline, agentBreakdownPipeline, agentTimeSeriesPipeline) = 
                BuildFlexibleMetricPipelines(baseConditions, metricMatchConditions, dateFormat);

            // Get totals
            var totalResult = await _collection.Aggregate<BsonDocument>(totalPipeline, cancellationToken: cancellationToken).FirstOrDefaultAsync(cancellationToken);
            var totalValue = totalResult != null && totalResult.Contains("total") ? ToInt64(totalResult["total"]) : 0;
            var requestCount = totalResult != null && totalResult.Contains("count") ? ToInt32(totalResult["count"]) : 0;

            // Get time series data
            var timeSeriesResult = await _collection.Aggregate<BsonDocument>(timeSeriesPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var timeSeriesData = timeSeriesResult.Select(doc => new TimeSeriesDataPoint
            {
                Date = ParseDateFromGrouping(doc["_id"].AsString, groupBy),
                Metrics = new UsageMetrics
                {
                    PrimaryCount = ToInt64(doc["total"]),
                    RequestCount = ToInt32(doc["count"])
                }
            }).ToList();

            // Get user breakdown
            var userBreakdownResult = await _collection.Aggregate<BsonDocument>(userBreakdownPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var userBreakdown = userBreakdownResult.Select(doc => new UserBreakdown
            {
                UserId = doc["_id"].AsString,
                UserName = doc["_id"].AsString,
                Metrics = new UsageMetrics
                {
                    PrimaryCount = ToInt64(doc["total"]),
                    RequestCount = ToInt32(doc["count"])
                }
            }).ToList();

            // Get agent breakdown
            var agentBreakdownResult = await _collection.Aggregate<BsonDocument>(agentBreakdownPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var agentBreakdown = agentBreakdownResult.Select(doc => new AgentBreakdown
            {
                AgentName = doc["_id"].AsString ?? "Unknown",
                Metrics = new UsageMetrics
                {
                    PrimaryCount = ToInt64(doc["total"]),
                    RequestCount = ToInt32(doc["count"])
                }
            }).ToList();

            // Get agent time series data
            var agentTimeSeriesResult = await _collection.Aggregate<BsonDocument>(agentTimeSeriesPipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            var agentTimeSeriesData = agentTimeSeriesResult.Select(doc => new AgentTimeSeriesDataPoint
            {
                Date = ParseDateFromGrouping(doc["date"].AsString, groupBy),
                AgentName = doc["agent"].AsString ?? "Unknown",
                Metrics = new UsageMetrics
                {
                    PrimaryCount = ToInt64(doc["total"]),
                    RequestCount = ToInt32(doc["count"])
                }
            }).ToList();

            // Determine unit from first result if available
            string? unit = null;
            if (timeSeriesResult.Any() && timeSeriesResult[0].Contains("unit"))
            {
                unit = timeSeriesResult[0]["unit"].AsString;
            }

            return new UsageEventsResponse
            {
                TenantId = tenantId,
                UserId = string.IsNullOrWhiteSpace(userId) || userId == "all" ? null : userId,
                Category = category,
                MetricType = metricType,
                Unit = unit,
                StartDate = startDate,
                EndDate = endDate,
                TotalValue = totalValue,
                TotalMetrics = new UsageMetrics
                {
                    PrimaryCount = totalValue,
                    RequestCount = requestCount
                },
                TimeSeriesData = timeSeriesData,
                UserBreakdown = userBreakdown,
                AgentBreakdown = agentBreakdown,
                AgentTimeSeriesData = agentTimeSeriesData
            };
        }, _logger, operationName: $"GetUsageEvents_{category}_{metricType}");
    }

    /// <summary>
    /// Extracts workflow name from workflow_id (for display/grouping purposes).
    /// Handles two formats:
    /// - With tenant: "tenant:AgentName:FlowName" -> workflow name is at index 2
    /// - Without tenant (A2A): "AgentName:FlowName" -> workflow name is at index 1
    /// </summary>
    private static string ExtractAgentName(string? workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return "Unknown";

        var parts = workflowId.Split(':');
        if (parts.Length >= 3)
        {
            // Format: tenant:AgentName:FlowName (with tenant prefix)
            return parts[2].Trim(); // Workflow name is the third part
        }
        else if (parts.Length >= 2)
        {
            // Format: AgentName:FlowName (A2A context, no tenant prefix)
            return parts[1].Trim(); // Workflow name is the second part
        }
        
        return "Unknown";
    }

    /// <summary>
    /// Builds aggregation pipelines for flexible metrics structure.
    /// Works with embedded metrics array using $unwind.
    /// </summary>
    private (BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[], BsonDocument[]) BuildFlexibleMetricPipelines(
        BsonDocument baseConditions, 
        BsonDocument metricMatchConditions,
        string dateFormat)
    {
        // Total pipeline
        var totalPipelineStages = new List<BsonDocument>
        {
            new BsonDocument("$match", baseConditions),
            new BsonDocument("$unwind", "$metrics")
        };
        
        if (metricMatchConditions.ElementCount > 0)
        {
            totalPipelineStages.Add(new BsonDocument("$match", metricMatchConditions));
        }
        
        totalPipelineStages.Add(new BsonDocument("$group", new BsonDocument
        {
            { "_id", BsonNull.Value },
            { "total", new BsonDocument("$sum", "$metrics.value") },
            { "count", new BsonDocument("$sum", 1) }
        }));

        // Time series pipeline
        var timeSeriesPipelineStages = new List<BsonDocument>
        {
            new BsonDocument("$match", baseConditions),
            new BsonDocument("$unwind", "$metrics")
        };
        
        if (metricMatchConditions.ElementCount > 0)
        {
            timeSeriesPipelineStages.Add(new BsonDocument("$match", metricMatchConditions));
        }
        
        timeSeriesPipelineStages.AddRange(new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$dateToString", new BsonDocument
                    {
                        { "format", dateFormat },
                        { "date", "$created_at" },
                        { "timezone", "UTC" }
                    })
                },
                { "total", new BsonDocument("$sum", "$metrics.value") },
                { "count", new BsonDocument("$sum", 1) },
                { "unit", new BsonDocument("$first", "$metrics.unit") }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        });

        // User breakdown pipeline
        var userBreakdownPipelineStages = new List<BsonDocument>
        {
            new BsonDocument("$match", baseConditions),
            new BsonDocument("$unwind", "$metrics")
        };
        
        if (metricMatchConditions.ElementCount > 0)
        {
            userBreakdownPipelineStages.Add(new BsonDocument("$match", metricMatchConditions));
        }
        
        userBreakdownPipelineStages.AddRange(new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$user_id" },
                { "total", new BsonDocument("$sum", "$metrics.value") },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("total", -1)),
            new BsonDocument("$limit", 100)
        });

        // Agent breakdown pipeline
        var agentBreakdownPipelineStages = new List<BsonDocument>
        {
            new BsonDocument("$match", baseConditions),
            new BsonDocument("$unwind", "$metrics")
        };
        
        if (metricMatchConditions.ElementCount > 0)
        {
            agentBreakdownPipelineStages.Add(new BsonDocument("$match", metricMatchConditions));
        }
        
        agentBreakdownPipelineStages.AddRange(new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) },
                { "total", new BsonDocument("$sum", "$metrics.value") },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("total", -1)),
            new BsonDocument("$limit", 100)
        });

        // Agent time series pipeline
        var agentTimeSeriesPipelineStages = new List<BsonDocument>
        {
            new BsonDocument("$match", baseConditions),
            new BsonDocument("$unwind", "$metrics")
        };
        
        if (metricMatchConditions.ElementCount > 0)
        {
            agentTimeSeriesPipelineStages.Add(new BsonDocument("$match", metricMatchConditions));
        }
        
        agentTimeSeriesPipelineStages.AddRange(new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "date", new BsonDocument("$dateToString", new BsonDocument
                            {
                                { "format", dateFormat },
                                { "date", "$created_at" },
                                { "timezone", "UTC" }
                            })
                        },
                        { "agent", new BsonDocument("$ifNull", new BsonArray { "$agent_name", "Unknown" }) }
                    }
                },
                { "total", new BsonDocument("$sum", "$metrics.value") },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "date", "$_id.date" },
                { "agent", "$_id.agent" },
                { "total", 1 },
                { "count", 1 }
            }),
            new BsonDocument("$sort", new BsonDocument("date", 1))
        });

        return (
            totalPipelineStages.ToArray(),
            timeSeriesPipelineStages.ToArray(),
            userBreakdownPipelineStages.ToArray(),
            agentBreakdownPipelineStages.ToArray(),
            agentTimeSeriesPipelineStages.ToArray()
        );
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
                        { "date", "$created_at" },
                        { "timezone", "UTC" }
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
                        { "input", new BsonDocument("$cond", new BsonDocument
                            {
                                { "if", new BsonDocument("$gte", new BsonArray
                                    {
                                        new BsonDocument("$size", new BsonDocument("$split", new BsonArray { "$workflow_id", ":" })),
                                        3
                                    })
                                },
                                { "then", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                },
                                { "else", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        1
                                    })
                                }
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
                        { "input", new BsonDocument("$cond", new BsonDocument
                            {
                                { "if", new BsonDocument("$gte", new BsonArray
                                    {
                                        new BsonDocument("$size", new BsonDocument("$split", new BsonArray { "$workflow_id", ":" })),
                                        3
                                    })
                                },
                                { "then", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                },
                                { "else", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        1
                                    })
                                }
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
                                { "date", "$created_at" },
                                { "timezone", "UTC" }
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
                        { "date", "$created_at" },
                        { "timezone", "UTC" }
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
                        { "input", new BsonDocument("$cond", new BsonDocument
                            {
                                { "if", new BsonDocument("$gte", new BsonArray
                                    {
                                        new BsonDocument("$size", new BsonDocument("$split", new BsonArray { "$workflow_id", ":" })),
                                        3
                                    })
                                },
                                { "then", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                },
                                { "else", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        1
                                    })
                                }
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
                        { "input", new BsonDocument("$cond", new BsonDocument
                            {
                                { "if", new BsonDocument("$gte", new BsonArray
                                    {
                                        new BsonDocument("$size", new BsonDocument("$split", new BsonArray { "$workflow_id", ":" })),
                                        3
                                    })
                                },
                                { "then", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                },
                                { "else", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        1
                                    })
                                }
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
                                { "date", "$created_at" },
                                { "timezone", "UTC" }
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
                        { "date", "$created_at" },
                        { "timezone", "UTC" }
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
                        { "input", new BsonDocument("$cond", new BsonDocument
                            {
                                { "if", new BsonDocument("$gte", new BsonArray
                                    {
                                        new BsonDocument("$size", new BsonDocument("$split", new BsonArray { "$workflow_id", ":" })),
                                        3
                                    })
                                },
                                { "then", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                },
                                { "else", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        1
                                    })
                                }
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
                        { "input", new BsonDocument("$cond", new BsonDocument
                            {
                                { "if", new BsonDocument("$gte", new BsonArray
                                    {
                                        new BsonDocument("$size", new BsonDocument("$split", new BsonArray { "$workflow_id", ":" })),
                                        3
                                    })
                                },
                                { "then", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        2
                                    })
                                },
                                { "else", new BsonDocument("$arrayElemAt", new BsonArray
                                    {
                                        new BsonDocument("$split", new BsonArray { "$workflow_id", ":" }),
                                        1
                                    })
                                }
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
                                { "date", "$created_at" },
                                { "timezone", "UTC" }
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
        "hour" => "%Y-%m-%dT%H:00:00Z",  // Hour: 2025-12-08T14:00:00Z
        "week" => "%Y-%U",                // Week: 2025-49 (Sunday-based) - no Z needed
        "month" => "%Y-%m",               // Month: 2025-12 - no Z needed
        _ => "%Y-%m-%dZ"                  // Day: 2025-12-08Z (default)
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

    public async Task<AvailableMetricsResponse> GetAvailableMetricsAsync(
        string tenantId, 
        CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Get all unique category/type combinations for this tenant
            var pipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument("tenant_id", tenantId)),
                new BsonDocument("$unwind", "$metrics"),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument
                        {
                            { "category", "$metrics.category" },
                            { "type", "$metrics.type" },
                            { "unit", "$metrics.unit" }
                        }
                    },
                    { "count", new BsonDocument("$sum", 1) }
                }),
                new BsonDocument("$sort", new BsonDocument
                {
                    { "_id.category", 1 },
                    { "_id.type", 1 }
                })
            };

            var results = await _collection.Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
            
            // Group by category and deduplicate metric types
            var categoriesDict = new Dictionary<string, List<MetricDefinition>>();
            var seenMetrics = new Dictionary<string, HashSet<string>>(); // Track seen (category, type) pairs
            
            foreach (var doc in results)
            {
                var idDoc = doc["_id"].AsBsonDocument;
                var category = idDoc["category"].AsString;
                var type = idDoc["type"].AsString;
                var unit = idDoc.Contains("unit") && !idDoc["unit"].IsBsonNull ? idDoc["unit"].AsString : "count";

                // Initialize tracking sets if needed
                if (!categoriesDict.ContainsKey(category))
                {
                    categoriesDict[category] = new List<MetricDefinition>();
                    seenMetrics[category] = new HashSet<string>();
                }

                // Only add if we haven't seen this metric type in this category
                if (seenMetrics[category].Add(type))
                {
                    categoriesDict[category].Add(new MetricDefinition
                    {
                        Type = type,
                        DisplayName = FormatDisplayName(type),
                        Unit = unit ?? "count"
                    });
                }
            }

            // Convert to response format
            var categories = categoriesDict.Select(kvp => new MetricCategoryInfo
            {
                CategoryId = kvp.Key,
                CategoryName = FormatDisplayName(kvp.Key),
                Metrics = kvp.Value
            }).ToList();

            return new AvailableMetricsResponse
            {
                Categories = categories
            };
        }, _logger, operationName: "GetAvailableMetrics");
    }

    private static string FormatDisplayName(string name)
    {
        // Convert snake_case to Title Case
        return string.Join(" ", name.Split('_')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1)));
    }

    private static DateTime ParseDateFromGrouping(string dateString, string groupBy)
    {
        return groupBy switch
        {
            // For hour: parse ISO string with 'Z' suffix as UTC
            "hour" => DateTime.ParseExact(dateString, "yyyy-MM-ddTHH:mm:ssZ", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal),
            "week" => ParseWeekDate(dateString),
            "month" => DateTime.SpecifyKind(DateTime.ParseExact(dateString, "yyyy-MM", null, System.Globalization.DateTimeStyles.None), DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(DateTime.ParseExact(dateString.TrimEnd('Z'), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None), DateTimeKind.Utc)
        };
    }

    private static DateTime ParseWeekDate(string weekString)
    {
        // Format: "2025-00" to "2025-53" (year-week from %U format)
        var parts = weekString.Split('-');
        var year = int.Parse(parts[0]);
        var week = int.Parse(parts[1]);
        
        // Calculate the first day of the year in UTC
        var jan1 = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        // Find the first Sunday of the year
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7;
        var firstSunday = jan1.AddDays(daysUntilSunday);
        
        // Add weeks
        return firstSunday.AddDays(week * 7);
    }
}

