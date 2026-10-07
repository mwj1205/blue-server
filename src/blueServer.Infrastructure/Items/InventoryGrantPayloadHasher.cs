using System.Security.Cryptography;
using System.Text.Json;

namespace blueServer.Infrastructure.Items;

public static class InventoryGrantPayloadHasher
{
    public static string Compute(InventoryGrantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentNullException.ThrowIfNull(request.OverflowMail);

        if (request.Items.Any(item => item is null))
        {
            throw new ArgumentException(
                "Inventory grant items must not contain null.",
                nameof(request));
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("items");
            writer.WriteStartArray();

            foreach (var item in request.Items.OrderBy(item => item.ItemTemplateId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("itemTemplateId", item.ItemTemplateId);
                writer.WriteNumber("amount", item.Amount);
                writer.WriteNumber("reasonType", (int)item.ReasonType);
                writer.WriteString("sourceId", item.SourceId);
                writer.WriteString("changedAt", item.ChangedAt);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("overflowMail");
            writer.WriteStartObject();
            writer.WriteNumber(
                "sourceType",
                (int)request.OverflowMail.SourceType);
            writer.WriteString("sourceId", request.OverflowMail.SourceId);
            writer.WriteString("title", request.OverflowMail.Title);
            writer.WriteString("body", request.OverflowMail.Body);
            writer.WriteString("sentAt", request.OverflowMail.SentAt);
            writer.WriteNumber(
                "retentionPolicy",
                (int)request.OverflowMail.RetentionPolicy);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
