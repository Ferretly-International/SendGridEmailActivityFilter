using System.Text.Json.Serialization;

namespace SendGridEmailActivityFilter.Core;

public record EmailActivityResponse(
    [property: JsonPropertyName("messages")] Message[]? Messages
);

public record Message(
    [property: JsonPropertyName("msg_id")]          string? MsgId,
    [property: JsonPropertyName("from_email")]      string? FromEmail,
    [property: JsonPropertyName("to_email")]        string? ToEmail,
    [property: JsonPropertyName("subject")]         string? Subject,
    [property: JsonPropertyName("status")]          string? Status,
    [property: JsonPropertyName("opens_count")]     int?    OpensCount,
    [property: JsonPropertyName("clicks_count")]    int?    ClicksCount,
    [property: JsonPropertyName("last_event_time")] string? LastEventTime
);
