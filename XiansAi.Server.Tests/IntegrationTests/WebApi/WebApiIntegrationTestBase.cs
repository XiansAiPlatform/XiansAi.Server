using Xunit;
using Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Features.WebApi.Auth;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Tests.IntegrationTests.WebApi;

public abstract class WebApiIntegrationTestBase : IntegrationTestBase
{
    protected WebApiIntegrationTestBase(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    protected async Task<HttpResponseMessage> GetAsync(string requestUri)
    {
        return await _client.GetAsync(requestUri);
    }

    protected async Task<HttpResponseMessage> PostAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.PostAsync(requestUri, content);
    }

    protected async Task<HttpResponseMessage> PutAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.PutAsync(requestUri, content);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string requestUri)
    {
        return await _client.DeleteAsync(requestUri);
    }

    protected async Task<T?> ReadAsJsonAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
} 