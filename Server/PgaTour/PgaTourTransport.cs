// Portions ported from pgatourPY (https://github.com/WalrusQuant/pgatourPY), MIT License.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GolfStatsApp.Server.PgaTour
{
    public class PgaTourOptions
    {
        public const string SectionName = "PgaTour";
        public string GraphQlUrl { get; set; } = "https://orchestrator.pgatour.com/graphql";
        public string RestUrl { get; set; } = "https://data-api.pgatour.com/";
        public string ConfigUrl { get; set; } = "https://orchestrator-config.pgatour.com/";
        public string ApiKey { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class PgaTourException : Exception
    {
        public PgaTourException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Low-level transport for the (unofficial) PGA Tour GraphQL, REST and config APIs.
    /// Retries, timeouts and rate limiting are applied by the resilience pipeline registered in DI.
    /// </summary>
    public class PgaTourTransport
    {
        public const string GraphQlClient = "PgaTour.GraphQL";
        public const string RestClient = "PgaTour.Rest";
        public const string ConfigClient = "PgaTour.Config";

        private static readonly ConcurrentDictionary<string, string> QueryCache = new();
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<PgaTourTransport> _logger;

        public PgaTourTransport(IHttpClientFactory factory, ILogger<PgaTourTransport> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<JsonNode?> GraphQlAsync(string operationName, object variables, CancellationToken ct = default)
        {
            var body = new { query = LoadQuery(operationName), variables, operationName };
            _logger.LogDebug("PGA GraphQL -> {Operation}", operationName);

            using var response = await _factory.CreateClient(GraphQlClient).PostAsJsonAsync("", body, ct);
            var json = await ReadJsonAsync(response, $"GraphQL {operationName}", ct);

            if (json?["errors"] is JsonArray errors && errors.Count > 0)
            {
                var messages = string.Join("; ", errors.Select(e => e?["message"]?.GetValue<string>()));
                throw new PgaTourException($"PGA Tour GraphQL error ({operationName}): {messages}");
            }
            return json?["data"];
        }

        public async Task<JsonNode?> RestAsync(string path, CancellationToken ct = default)
        {
            _logger.LogDebug("PGA REST -> {Path}", path);
            using var response = await _factory.CreateClient(RestClient).GetAsync(path, ct);
            return await ReadJsonAsync(response, $"REST {path}", ct);
        }

        public async Task<JsonNode?> ConfigAsync(string path, CancellationToken ct = default)
        {
            using var response = await _factory.CreateClient(ConfigClient).GetAsync(path, ct);
            return await ReadJsonAsync(response, $"CONFIG {path}", ct);
        }

        /// <summary>Decodes the base64 + gzip JSON payloads returned by "*Compressed" operations.</summary>
        public static JsonNode? DecompressPayload(string? payload)
        {
            if (string.IsNullOrEmpty(payload))
                throw new PgaTourException("Compressed payload was empty");
            try
            {
                using var input = new MemoryStream(Convert.FromBase64String(payload));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                return JsonNode.Parse(gzip);
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
            {
                throw new PgaTourException($"Failed to decode compressed payload: {ex.Message}", ex);
            }
        }

        private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, string context, CancellationToken ct)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new PgaTourException($"{context} failed ({(int)response.StatusCode}): {Snippet(text)}");
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new PgaTourException($"Non-JSON response from {context}: {Snippet(text)}", ex);
            }
        }

        private static string Snippet(string text) => text.Length > 200 ? text[..200] : text;

        private static string LoadQuery(string operationName) => QueryCache.GetOrAdd(operationName, name =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith($".{name}.graphql", StringComparison.Ordinal))
                ?? throw new PgaTourException($"GraphQL query not found: {name}");
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }
}
