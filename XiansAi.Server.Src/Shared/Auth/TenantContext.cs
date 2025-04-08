using XiansAi.Server.GenAi;
using XiansAi.Server.Temporal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Shared.Auth
{
    public interface ITenantContext
    {
        string TenantId { get; set; }   
        string? LoggedInUser { get; set; }
        IEnumerable<string> AuthorizedTenantIds { get; set; }
        
        TemporalConfig GetTemporalConfig();
        OpenAIConfig GetOpenAIConfig();
    }

    public class TenantContext : ITenantContext
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<TenantContext> _logger;

        public required string TenantId { get; set; }
        public string? LoggedInUser { get; set; }
        public IEnumerable<string> AuthorizedTenantIds { get; set; } = new List<string>();

        public TenantContext(IConfiguration configuration, ILogger<TenantContext> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public TemporalConfig GetTemporalConfig() 
        { 
            ValidateTenantId();

            // get the temporal config for the tenant
            var temporalConfig = _configuration.GetSection($"Tenants:{TenantId}:Temporal").Get<TemporalConfig>();

            if (temporalConfig == null) {
                // fallback to the root temporal config
                temporalConfig = _configuration.GetSection("Temporal").Get<TemporalConfig>();
            }
            // we cant share the temporal config between tenants, so if it is not found, throw an error
            if (temporalConfig == null) {
                throw new InvalidOperationException($"Temporal configuration for tenant {TenantId} not found");
            }

            if (string.IsNullOrEmpty(temporalConfig.CertificateBase64)) 
                throw new InvalidOperationException($"CertificateBase64 is required for tenant {TenantId}");
            if (string.IsNullOrEmpty(temporalConfig.PrivateKeyBase64)) 
                throw new InvalidOperationException($"PrivateKeyBase64 is required for tenant {TenantId}");
            if (temporalConfig.FlowServerUrl == null) 
                throw new InvalidOperationException($"FlowServerUrl is required for tenant {TenantId}");
            
            return temporalConfig;
        }

        public OpenAIConfig GetOpenAIConfig() 
        { 
            ValidateTenantId();

            var openAIConfig = _configuration.GetSection($"Tenants:{TenantId}:OpenAI").Get<OpenAIConfig>();

            if (openAIConfig == null) {
                // if tenant is not using a different api key, use the root config
                openAIConfig = _configuration.GetSection("OpenAI").Get<OpenAIConfig>() 
                    ?? throw new InvalidOperationException("OpenAI configuration not found");
                return openAIConfig;
            } else {
                // if tenant is using a different api key, use the tenant config
                if (openAIConfig.ApiKey == null) {
                    openAIConfig.ApiKey = _configuration.GetSection("OpenAI:ApiKey").Value
                        ?? throw new InvalidOperationException("OpenAI api key not found");
                }

                if (openAIConfig.Model == null) {
                    openAIConfig.Model = _configuration.GetSection("OpenAI:Model").Value
                        ?? throw new InvalidOperationException("OpenAI model not found");
                }
                return openAIConfig;
            }
        }

        private void ValidateTenantId()
        {
            if (string.IsNullOrEmpty(TenantId)) 
                throw new InvalidOperationException("TenantId is required");
        }
    }
} 