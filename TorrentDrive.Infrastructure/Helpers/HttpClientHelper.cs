using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TorrentDrive.Infrastructure.Helpers;

public record ApiResponse<T>(bool Success, T? Data, string? Error, int StatusCode);

public class HttpClientHelper(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public void SetHeader(string key, string value)
    {
        httpClient.DefaultRequestHeaders.Remove(key);
        httpClient.DefaultRequestHeaders.Add(key, value);
    }

    public void SetBaseAddress(string baseUrl)
    {
        httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<ApiResponse<T>> GetAsync<T>(string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            return await ParseResponse<T>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>(false, default, ex.Message, 0);
        }
    }

    public async Task<ApiResponse<T>> PostAsync<T>(string url, object? body = null)
    {
        try
        {
            HttpContent? content = null;
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await httpClient.PostAsync(url, content);
            return await ParseResponse<T>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>(false, default, ex.Message, 0);
        }
    }

    public async Task<ApiResponse<T>> PutAsync<T>(string url, object? body = null)
    {
        try
        {
            HttpContent? content = null;
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await httpClient.PutAsync(url, content);
            return await ParseResponse<T>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>(false, default, ex.Message, 0);
        }
    }

    public async Task<ApiResponse<T>> DeleteAsync<T>(string url)
    {
        try
        {
            var response = await httpClient.DeleteAsync(url);
            return await ParseResponse<T>(response);
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>(false, default, ex.Message, 0);
        }
    }

    private static async Task<ApiResponse<T>> ParseResponse<T>(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            return new ApiResponse<T>(false, default, errorContent, statusCode);
        }

        try
        {
            var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            return new ApiResponse<T>(true, data, null, statusCode);
        }
        catch (JsonException ex)
        {
            return new ApiResponse<T>(false, default, $"Failed to deserialize response: {ex.Message}", statusCode);
        }
    }
}

