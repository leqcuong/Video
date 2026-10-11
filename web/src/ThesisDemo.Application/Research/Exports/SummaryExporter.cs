using System.Text;
using System.Text.Json;

namespace ThesisDemo.Application.Research.Exports;

public static class SummaryExporter
{
    public static byte[] Render(string kind, string summaryJson, string format)
    {
        using var document = JsonDocument.Parse(summaryJson);
        var rows = new List<(string Metric, string Value)>();
        Flatten(document.RootElement, "", rows);
        var content = format switch
        {
            "json" => JsonSerializer.Serialize(new { kind, metrics = document.RootElement }, new JsonSerializerOptions { WriteIndented = true }),
            "csv" => "metric,value\r\n" + string.Join("\r\n", rows.Select(x => $"{Csv(x.Metric)},{Csv(x.Value)}")) + "\r\n",
            "md" => $"# {kind} (recorded)\n\n| Metric | Value |\n| --- | --- |\n" + string.Join("\n", rows.Select(x => $"| {Markdown(x.Metric)} | {Markdown(x.Value)} |")) + "\n",
            _ => throw new ArgumentException("Unsupported export format.")
        };
        return Encoding.UTF8.GetBytes(content);
    }

    private static void Flatten(JsonElement value, string prefix, List<(string, string)> rows)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject()) Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", rows);
        }
        else rows.Add((prefix, value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText()));
    }
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Markdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
