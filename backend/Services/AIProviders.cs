using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using System.Diagnostics;

namespace TaxFlow.Api.Services;

public sealed record InvoiceDocumentInput(string FileName, string ContentType, byte[] Bytes);

public sealed class InvoiceExtractionResult
{
    public int PageCount { get; init; } = 1;
    public string Provider { get; init; } = "Demo";
    public string SupplierName { get; init; } = string.Empty;
    public string? SupplierTaxCode { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string? Serial { get; init; }
    public DateTime? InvoiceDate { get; init; }
    public decimal Subtotal { get; init; }
    public decimal VatAmount { get; init; }
    public decimal Total { get; init; }
    public decimal ConfidenceScore { get; init; }
    public IReadOnlyList<ExtractedInvoiceItem> Items { get; init; } = Array.Empty<ExtractedInvoiceItem>();
    public IReadOnlyList<OcrEvidence> Evidence { get; init; } = Array.Empty<OcrEvidence>();
    public IReadOnlyList<string> ProviderWarnings { get; init; } = Array.Empty<string>();
}

public sealed record OcrEvidence(string Field, string? Value, int Page, decimal X, decimal Y, decimal Width, decimal Height, decimal? Confidence = null, string? Source = null);

public interface IAIProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken);
}

public sealed class DemoAIProvider : IAIProvider
{
    public string Name => "DemoAIProvider";
    public bool IsAvailable => true;

    public Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        var stem = Path.GetFileNameWithoutExtension(input.FileName);
        var taxCode = Regex.Match(stem, @"(?<!\d)(\d{10}|\d{13})(?!\d)").Value;
        var invoice = Regex.Match(stem, @"(?:HD|INV|INVOICE)[-_ ]?([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        var result = new InvoiceExtractionResult
        {
            Provider = Name,
            SupplierName = "Cần người dùng xác nhận",
            SupplierTaxCode = string.IsNullOrWhiteSpace(taxCode) ? null : taxCode,
            InvoiceNumber = invoice.Success ? invoice.Groups[1].Value : string.Empty,
            ConfidenceScore = invoice.Success || !string.IsNullOrEmpty(taxCode) ? 0.42m : 0.20m,
            ProviderWarnings = new[]
            {
                "Chế độ Demo không đọc nội dung hình ảnh/PDF; chỉ nhận diện gợi ý từ tên tệp.",
                "Hãy kiểm tra và nhập lại toàn bộ số liệu trước khi xác nhận."
            }
        };
        return Task.FromResult(result);
    }
}

public sealed class OpenAIProvider(HttpClient httpClient, IConfiguration configuration) : IAIProvider
{
    public string Name => "OpenAIProvider";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(configuration["OPENAI_API_KEY"]);

    public async Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        var key = configuration["OPENAI_API_KEY"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OPENAI_API_KEY chưa được cấu hình.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var dataUrl = $"data:{input.ContentType};base64,{Convert.ToBase64String(input.Bytes)}";
        object fileContent = input.ContentType == "application/pdf"
            ? new { type = "input_file", filename = input.FileName, file_data = dataUrl }
            : new { type = "input_image", image_url = dataUrl };
        var prompt = """
            Đọc hóa đơn Việt Nam này và trả về CHỈ một JSON object theo schema:
            supplierName, supplierTaxCode, invoiceNumber, serial, invoiceDate (ISO yyyy-MM-dd),
            subtotal, vatAmount, total, confidenceScore (0..1), items[] gồm description, unit,
            quantity, unitPrice, vatRate, vatAmount, total. Không suy đoán trường không nhìn rõ;
            dùng chuỗi rỗng/null hoặc 0 và giảm confidenceScore.
            """;
        var body = new
        {
            model = configuration["OPENAI_MODEL"] ?? "gpt-4.1-mini",
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new object[] { new { type = "input_text", text = prompt }, fileContent }
                }
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI trả về lỗi {(int)response.StatusCode}.");

        using var root = JsonDocument.Parse(responseText);
        var outputText = TryReadOutputText(root.RootElement)
                         ?? throw new InvalidOperationException("Không tìm thấy kết quả OCR trong phản hồi OpenAI.");
        var cleanJson = ExtractJsonObject(outputText);
        var parsed = JsonSerializer.Deserialize<OpenAIInvoiceResult>(cleanJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        }) ?? throw new InvalidOperationException("Không thể phân tích JSON OCR.");

        return new InvoiceExtractionResult
        {
            Provider = Name,
            PageCount = input.ContentType == "application/pdf" ? Math.Max(1, parsed.PageCount) : 1,
            SupplierName = parsed.SupplierName ?? string.Empty,
            SupplierTaxCode = parsed.SupplierTaxCode,
            InvoiceNumber = parsed.InvoiceNumber ?? string.Empty,
            Serial = parsed.Serial,
            InvoiceDate = DateTime.TryParse(parsed.InvoiceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            Subtotal = parsed.Subtotal,
            VatAmount = parsed.VatAmount,
            Total = parsed.Total,
            ConfidenceScore = Math.Clamp(parsed.ConfidenceScore, 0, 1),
            Items = parsed.Items ?? Array.Empty<ExtractedInvoiceItem>()
        };
    }

    private static string? TryReadOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString();
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString();
        }
        return null;
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private sealed class OpenAIInvoiceResult
    {
        public int PageCount { get; init; } = 1;
        public string? SupplierName { get; init; }
        public string? SupplierTaxCode { get; init; }
        public string? InvoiceNumber { get; init; }
        public string? Serial { get; init; }
        public string? InvoiceDate { get; init; }
        public decimal Subtotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal Total { get; init; }
        public decimal ConfidenceScore { get; init; }
        public IReadOnlyList<ExtractedInvoiceItem>? Items { get; init; }
    }
}

public sealed class GeminiAIProvider(HttpClient httpClient, IConfiguration configuration) : IAIProvider
{
    public string Name => "GeminiAIProvider";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"]);

    public async Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        var key = configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("GEMINI_API_KEY chưa được cấu hình.");

        var model = configuration["GEMINI_MODEL"] ?? "gemini-3.6-flash";
        var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(key)}";

        var base64Data = Convert.ToBase64String(input.Bytes);
        var mimeType = input.ContentType == "application/pdf" ? "application/pdf" : input.ContentType;

                var prompt = """
                        Bạn là TAXFLOW-AI INVOICE OCR ENGINE. Đọc toàn bộ ảnh/PDF, kể cả mọi trang,
                        rồi chỉ trả về MỘT JSON object hợp lệ, không Markdown và không giải thích ngoài JSON.
                        Mục tiêu: độ chính xác > đầy đủ > tốc độ. Không được bịa, sửa số cho hợp lý,
                        hoặc suy luận thay dữ liệu không nhìn thấy. Trường không thấy hoặc không chắc phải
                        là null (riêng các trường số bắt buộc của schema dùng 0 và thêm warning).

                        JSON bắt buộc:
                        {
                            "pageCount": 1, "documentType": null, "invoiceRegime": null,
                            "supplierName": null, "supplierTaxCode": null,
                            "invoiceNumber": null, "serial": null, "invoiceDate": null,
                            "subtotal": 0, "vatAmount": 0, "total": 0,
                            "confidenceScore": 0, "items": [], "evidence": [], "providerWarnings": [],
                            "softwareProviderName": null, "validationWarnings": []
                        }
                        Mỗi item gồm: description, unit, quantity, unitPrice, vatRate, vatAmount, total.

                        Quy tắc bắt buộc:
                        - Đọc đầu trang, bảng hàng hóa, tổng tiền, chân trang, QR và tất cả các trang.
                        - supplierName/taxCode phải thuộc Đơn vị bán hàng/Người bán/Seller.
                            Không lấy tên hoặc MST của “Giải pháp hóa đơn điện tử được cung cấp bởi”,
                            Powered by, phần mềm, website tra cứu, ngân hàng hay bên vận chuyển.
                            Đưa đơn vị phần mềm vào softwareProviderName.
                        - invoiceNumber chỉ lấy sau nhãn Số/Số hóa đơn/No./Invoice Number; giữ nguyên số 0 đầu.
                            Không dùng ký hiệu, MST, mã tra cứu hoặc mã cơ quan thuế làm số hóa đơn.
                        - serial chỉ lấy sau nhãn Ký hiệu; invoiceDate ưu tiên Ngày lập/Ngày hóa đơn,
                            định dạng yyyy-MM-dd, không dùng Ký ngày nếu có ngày lập riêng.
                        - Trích xuất tất cả dòng hàng hóa/dịch vụ, ghép tên bị xuống dòng, không bỏ dòng.
                            Đọc quantity, unitPrice và total riêng; không nhầm đơn giá với thành tiền.
                        - Nhận diện chiết khấu, phí, nhiều mức VAT, phương thức thanh toán, tiền bằng chữ,
                            điều chỉnh/thay thế trong providerWarnings nếu schema chính không có trường đó.
                            Không mặc định VAT 10% hoặc buyer là lỗi với hóa đơn bán lẻ.
                        - Kiểm tra quantity * unitPrice, tổng dòng, subtotal + VAT và total. Nếu lệch,
                            giữ nguyên số đọc được và ghi warning, không tự sửa.
                        - confidenceScore từ 0 đến 1; giảm điểm khi ảnh mờ, thiếu vùng, vai trò không rõ,
                            trường quan trọng không chắc hoặc phép tính không khớp.
                        - TẤT CẢ trường số phải là JSON number, không phải chuỗi. Ví dụ: vatRate=10, không phải "10%";
                            unitPrice=2000000, không phải "2 triệu VND"; total=70000000, không phải "70.000.000 VND".
                            Nếu không đọc được số thì dùng 0 và thêm warning.
                        - Các số tiền là số thuần, không dấu phân cách; số lượng thập phân được giữ nguyên.
                        - evidence là các vùng nhận diện quan trọng, tọa độ tương đối từ 0 đến 1:
                                                    {"field":"invoiceNumber","value":"...","page":1,"x":0.1,"y":0.2,"width":0.2,"height":0.05}.
                                                    Chỉ trả vùng khi thực sự xác định được; không tự đoán tọa độ.
                        """;

        var request = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = base64Data
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0m,
                top_p = 0.8m,
                top_k = 32,
                max_output_tokens = 8192,
                response_mime_type = "application/json"
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini API trả về lỗi {(int)response.StatusCode}: {responseText}");

        using var root = JsonDocument.Parse(responseText);
        var candidates = root.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini không trả về kết quả.");

        var firstCandidate = candidates[0];
        var content = firstCandidate.GetProperty("content");
        var parts = content.GetProperty("parts");
        if (parts.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini không trả về phần nội dung.");

        var textPart = string.Join("", parts.EnumerateArray()
            .Where(part => part.TryGetProperty("text", out _))
            .Select(part => part.GetProperty("text").GetString()))
            .Trim();
        if (string.IsNullOrWhiteSpace(textPart))
            throw new InvalidOperationException("Không tìm thấy text trong response.");
        var cleanJson = ExtractJsonObject(textPart);
        var geminiJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        geminiJsonOptions.Converters.Add(new FlexibleDecimalConverter());
        geminiJsonOptions.Converters.Add(new FlexibleNullableDecimalConverter());

        var parsed = JsonSerializer.Deserialize<GeminiInvoiceResult>(cleanJson, geminiJsonOptions)
                     ?? throw new InvalidOperationException("Không thể phân tích JSON từ Gemini.");

        return new InvoiceExtractionResult
        {
            Provider = Name,
            PageCount = input.ContentType == "application/pdf" ? Math.Max(1, parsed.PageCount) : 1,
            SupplierName = parsed.SupplierName ?? string.Empty,
            SupplierTaxCode = parsed.SupplierTaxCode,
            InvoiceNumber = parsed.InvoiceNumber ?? string.Empty,
            Serial = parsed.Serial,
            InvoiceDate = ParseInvoiceDate(parsed.InvoiceDate),
            Subtotal = parsed.Subtotal,
            VatAmount = parsed.VatAmount,
            Total = parsed.Total,
            ConfidenceScore = Math.Clamp(parsed.ConfidenceScore, 0, 1),
            Items = parsed.Items ?? Array.Empty<ExtractedInvoiceItem>(),
            Evidence = parsed.Evidence ?? Array.Empty<OcrEvidence>(),
            ProviderWarnings = (parsed.ProviderWarnings ?? Array.Empty<string>())
                .Concat(parsed.ValidationWarnings ?? Array.Empty<string>())
                .Concat(string.IsNullOrWhiteSpace(parsed.SoftwareProviderName)
                    ? Array.Empty<string>()
                    : new[] { $"Nhà cung cấp phần mềm: {parsed.SoftwareProviderName}" })
                .ToArray()
        };
    }

    private static DateTime? ParseInvoiceDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var formats = new[]
        {
            "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy", "d-M-yyyy", "yyyy/MM/dd"
        };

        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(value, new CultureInfo("vi-VN"), DateTimeStyles.None, out var fallback)
            ? fallback
            : null;
    }

    private static decimal ParseFlexibleDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;

        var value = raw.Trim()
            .Replace("VNĐ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("VND", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₫", "", StringComparison.OrdinalIgnoreCase)
            .Replace("đ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        decimal multiplier = 1m;
        if (value.Contains("tỷ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("tỉ", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000_000m;
            value = Regex.Replace(value, "(?i)tỷ|tỉ", "").Trim();
        }
        else if (value.Contains("triệu", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000m;
            value = Regex.Replace(value, "(?i)triệu", "").Trim();
        }
        else if (value.Contains("nghìn", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("ngàn", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000m;
            value = Regex.Replace(value, "(?i)nghìn|ngàn", "").Trim();
        }

        value = Regex.Replace(value, @"[^0-9,\.\-+]", "");
        if (string.IsNullOrWhiteSpace(value)) return 0m;

        // 70.000.000 or 70,000,000 -> integer thousands grouping.
        if (Regex.IsMatch(value, @"^[+-]?\d{1,3}(\.\d{3})+$"))
            value = value.Replace(".", "");
        else if (Regex.IsMatch(value, @"^[+-]?\d{1,3}(,\d{3})+$"))
            value = value.Replace(",", "");
        else if (value.Contains(',') && !value.Contains('.'))
            value = value.Replace(',', '.');
        else if (value.Contains(',') && value.Contains('.'))
        {
            // Keep the right-most separator as decimal separator and treat the other as thousands separator.
            var lastComma = value.LastIndexOf(',');
            var lastDot = value.LastIndexOf('.');
            if (lastComma > lastDot)
                value = value.Replace(".", "").Replace(',', '.');
            else
                value = value.Replace(",", "");
        }

        return decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture, out var parsed)
            ? parsed * multiplier
            : 0m;
    }

    private sealed class FlexibleDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numeric))
                return numeric;
            if (reader.TokenType == JsonTokenType.String)
                return ParseFlexibleDecimal(reader.GetString());
            if (reader.TokenType == JsonTokenType.Null)
                return 0m;

            using var doc = JsonDocument.ParseValue(ref reader);
            return ParseFlexibleDecimal(doc.RootElement.ToString());
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }

    private sealed class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
    {
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numeric))
                return numeric;
            if (reader.TokenType == JsonTokenType.String)
            {
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw)) return null;
                return ParseFlexibleDecimal(raw);
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            var text = doc.RootElement.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : ParseFlexibleDecimal(text);
        }

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private sealed class GeminiInvoiceResult
    {
        public int PageCount { get; init; } = 1;
        public string? DocumentType { get; init; }
        public string? InvoiceRegime { get; init; }
        public string? SupplierName { get; init; }
        public string? SupplierTaxCode { get; init; }
        public string? InvoiceNumber { get; init; }
        public string? Serial { get; init; }
        public string? InvoiceDate { get; init; }
        public decimal Subtotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal Total { get; init; }
        public decimal ConfidenceScore { get; init; }
        public IReadOnlyList<ExtractedInvoiceItem>? Items { get; init; }
        public IReadOnlyList<OcrEvidence>? Evidence { get; init; }
        public string? SoftwareProviderName { get; init; }
        public IReadOnlyList<string>? ProviderWarnings { get; init; }
        public IReadOnlyList<string>? ValidationWarnings { get; init; }
    }
}

public sealed class PaddleOCRProvider(ILogger<PaddleOCRProvider> logger) : IAIProvider
{
    public string Name => "PaddleOCRProvider";
    public bool IsAvailable
    {
        get
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "-c \"import paddleocr; print('ok')\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                return proc?.WaitForExit(3000) == true && proc.ExitCode == 0;
            }
            catch { return false; }
        }
    }

    public async Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        var text = await ExtractTextWithPaddleOCR(input.Bytes, cancellationToken);
        
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("PaddleOCR không thể trích xuất text từ tài liệu.");

        return ParseInvoiceFromText(text);
    }

    private async Task<string> ExtractTextWithPaddleOCR(byte[] bytes, CancellationToken ct)
    {
        var tempImagePath = Path.Combine(Path.GetTempPath(), $"invoice_{Guid.NewGuid():N}.tmp");
        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllBytesAsync(tempImagePath, bytes, ct);

            var pythonScript = $@"
import json
try:
    from paddleocr import PaddleOCR
    ocr = PaddleOCR(use_angle_cls=True, lang='vi')
    result = ocr.ocr('{tempImagePath}', cls=True)
    text = '\n'.join([line[0][1] for block in result for line in block]) if result else ''
    with open('{tempOutputPath}', 'w', encoding='utf-8') as f:
        json.dump({{'text': text}}, f, ensure_ascii=False)
except Exception as e:
    with open('{tempOutputPath}', 'w', encoding='utf-8') as f:
        json.dump({{'error': str(e)}}, f)
";
            var scriptPath = Path.Combine(Path.GetTempPath(), $"paddle_{Guid.NewGuid():N}.py");
            await File.WriteAllTextAsync(scriptPath, pythonScript, ct);

            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không thể khởi động Python.");
            if (!await WaitForProcessAsync(proc, 60000, ct))
                throw new InvalidOperationException("PaddleOCR hết timeout.");

            if (!File.Exists(tempOutputPath))
                return string.Empty;

            var json = await File.ReadAllTextAsync(tempOutputPath, ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var textProp) 
                ? textProp.GetString() ?? string.Empty 
                : string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning("PaddleOCR extraction failed: {Message}", ex.Message);
            return string.Empty;
        }
        finally
        {
            try { File.Delete(tempImagePath); } catch { }
            try { File.Delete(tempOutputPath); } catch { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), $"paddle_*.py")); } catch { }
        }
    }

    private static async Task<bool> WaitForProcessAsync(Process proc, int timeoutMs, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try { await Task.Run(() => proc.WaitForExit(), cts.Token); return true; }
        catch (OperationCanceledException) { try { proc.Kill(); } catch { } return false; }
    }

    private InvoiceExtractionResult ParseInvoiceFromText(string text)
    {
        var warnings = new List<string>();
        var confidence = 0.75m; // PaddleOCR cho tiếng Việt có độ chính xác cao

        // Parse các trường từ text
        var taxCodeMatch = Regex.Match(text, @"MST[:\s]+([\d]{10}|[\d]{13})", RegexOptions.IgnoreCase);
        var taxCode = taxCodeMatch.Success ? taxCodeMatch.Groups[1].Value : null;

        var supplierMatch = Regex.Match(text, @"(?:Đơn vị bán|Seller|Công ty|CÔNG TY)[:\s]*([A-ZỀẢ\s]+?)(?:\n|MST|Mã số|Địa chỉ)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var supplier = supplierMatch.Success ? supplierMatch.Groups[1].Value.Trim() : "Cần người dùng xác nhận";

        var invoiceNumMatch = Regex.Match(text, @"(?:Số HĐ?|Number|No\.?|Số hóa đơn)[:\s]*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
        var invoiceNum = invoiceNumMatch.Success ? invoiceNumMatch.Groups[1].Value.Trim() : string.Empty;

        var dateMatch = Regex.Match(text, @"Ngày[:\s]+(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})", RegexOptions.IgnoreCase);
        var invoiceDate = dateMatch.Success && DateTime.TryParse($"{dateMatch.Groups[3].Value}-{dateMatch.Groups[2].Value}-{dateMatch.Groups[1].Value}", out var dt) ? (DateTime?)dt : null;

        var totalMatch = Regex.Match(text, @"(?:TỔNG|Total|Thành tiền|TỔNG CỘNG)[:\s]*([0-9\.,]+)\s*(?:VND|đ|₫)?", RegexOptions.IgnoreCase);
        var totalStr = totalMatch.Success ? Regex.Replace(totalMatch.Groups[1].Value, @"[,.](?=\d{3}(?:,|\.|\s|$))", "") : "0";
        var total = decimal.TryParse(totalStr, out var t) ? t : 0m;

        var vatMatch = Regex.Match(text, @"(?:VAT|Thuế|Thuế GTGT)[:\s]*([0-9\.,]+)\s*(?:VND|đ|₫)?", RegexOptions.IgnoreCase);
        var vatStr = vatMatch.Success ? Regex.Replace(vatMatch.Groups[1].Value, @"[,.](?=\d{3}(?:,|\.|\s|$))", "") : "0";
        var vat = decimal.TryParse(vatStr, out var v) ? v : 0m;

        var subtotal = total - vat;

        if (string.IsNullOrWhiteSpace(invoiceNum)) { warnings.Add("Không tìm thấy số hóa đơn rõ ràng."); confidence -= 0.05m; }
        if (taxCode == null) { warnings.Add("Không tìm thấy mã số thuế."); confidence -= 0.10m; }
        if (invoiceDate == null) { warnings.Add("Không tìm thấy ngày hóa đơn."); confidence -= 0.05m; }
        if (total == 0) { warnings.Add("Không tìm thấy tổng tiền."); confidence -= 0.15m; }

        return new InvoiceExtractionResult
        {
            Provider = Name,
            SupplierName = supplier,
            SupplierTaxCode = taxCode,
            InvoiceNumber = invoiceNum,
            InvoiceDate = invoiceDate,
            Subtotal = subtotal,
            VatAmount = vat,
            Total = total,
            ConfidenceScore = Math.Max(0.20m, confidence),
            ProviderWarnings = warnings
        };
    }
}

public sealed class TesseractOCRProvider(ILogger<TesseractOCRProvider> logger) : IAIProvider
{
    public string Name => "TesseractOCRProvider";
    public bool IsAvailable
    {
        get
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tesseract",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                return proc?.WaitForExit(2000) == true;
            }
            catch { return false; }
        }
    }

    public async Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        var text = input.ContentType == "application/pdf"
            ? await ExtractPdfText(input.Bytes, cancellationToken)
            : await ExtractImageText(input.Bytes, input.ContentType, cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Tesseract không thể trích xuất text từ tài liệu.");

        return ParseInvoiceFromText(text);
    }

    private async Task<string> ExtractImageText(byte[] bytes, string contentType, CancellationToken ct)
    {
        var tempImagePath = Path.Combine(Path.GetTempPath(), $"invoice_{Guid.NewGuid():N}.tmp");
        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"invoice_{Guid.NewGuid():N}");

        try
        {
            await File.WriteAllBytesAsync(tempImagePath, bytes, ct);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = $"\"{tempImagePath}\" \"{tempOutputPath}\" -l vie --psm 1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Không thể khởi động Tesseract.");
            if (!await WaitForProcessAsync(proc, 30000, ct))
                throw new InvalidOperationException("Tesseract hết timeout.");

            var outputFile = $"{tempOutputPath}.txt";
            return File.Exists(outputFile) ? await File.ReadAllTextAsync(outputFile, ct) : string.Empty;
        }
        finally
        {
            try { File.Delete(tempImagePath); } catch { }
            try { File.Delete($"{tempOutputPath}.txt"); } catch { }
        }
    }

    private async Task<string> ExtractPdfText(byte[] bytes, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"invoice_{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, ct);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pdftotext",
                Arguments = $"\"{tempPath}\" -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return string.Empty;
            return await proc.StandardOutput.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning("PDF extraction failed: {Message}", ex.Message);
            return string.Empty;
        }
        finally { try { File.Delete(tempPath); } catch { } }
    }

    private static async Task<bool> WaitForProcessAsync(System.Diagnostics.Process proc, int timeoutMs, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try { await Task.Run(() => proc.WaitForExit(), cts.Token); return true; }
        catch (OperationCanceledException) { try { proc.Kill(); } catch { } return false; }
    }

    private InvoiceExtractionResult ParseInvoiceFromText(string text)
    {
        var warnings = new List<string>();
        var confidence = 0.65m;

        var taxCodeMatch = Regex.Match(text, @"MST[:\s]+([\d]{10}|[\d]{13})", RegexOptions.IgnoreCase);
        var taxCode = taxCodeMatch.Success ? taxCodeMatch.Groups[1].Value : null;

        var supplierMatch = Regex.Match(text, @"(?:Đơn vị bán|Seller|Công ty)[:\s]*([A-ZỀẢ\s]+?)(?:\n|MST|Mã số)", RegexOptions.IgnoreCase);
        var supplier = supplierMatch.Success ? supplierMatch.Groups[1].Value.Trim() : "Cần người dùng xác nhận";

        var invoiceNumMatch = Regex.Match(text, @"(?:Số HĐ?|Number|No\.?)[:\s]*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
        var invoiceNum = invoiceNumMatch.Success ? invoiceNumMatch.Groups[1].Value.Trim() : string.Empty;

        var dateMatch = Regex.Match(text, @"Ngày[:\s]+(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})", RegexOptions.IgnoreCase);
        var invoiceDate = dateMatch.Success && DateTime.TryParse($"{dateMatch.Groups[3].Value}-{dateMatch.Groups[2].Value}-{dateMatch.Groups[1].Value}", out var dt) ? (DateTime?)dt : null;

        static decimal ParseMoney(string raw)
        {
            var cleaned = Regex.Replace(raw ?? string.Empty, @"[^0-9]", "");
            return decimal.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0m;
        }

        static decimal FindLabeledMoney(string source, params string[] labels)
        {
            var label = string.Join("|", labels.Select(Regex.Escape));
            var sameLine = Regex.Match(source, $@"(?im)(?:{label})[^\r\n]{{0,100}}?(\d{{1,3}}(?:[.,]\d{{3}})+(?!\d)|\d{{4,}})\s*(?:VND|VNĐ|đ|₫)?", RegexOptions.IgnoreCase);
            if (sameLine.Success) return ParseMoney(sameLine.Groups[1].Value);

            var nextLine = Regex.Match(source, $@"(?im)(?:{label})[^\r\n]*[\r\n]+[^\r\n]*?(\d{{1,3}}(?:[.,]\d{{3}})+(?!\d)|\d{{4,}})\s*(?:VND|VNĐ|đ|₫)?", RegexOptions.IgnoreCase);
            return nextLine.Success ? ParseMoney(nextLine.Groups[1].Value) : 0m;
        }

        var total = FindLabeledMoney(text, "TỔNG TIỀN THANH TOÁN", "TỔNG CỘNG", "CỘNG TIỀN BÁN HÀNG HÓA, DỊCH VỤ", "TỔNG", "TOTAL PAYMENT", "TOTAL AMOUNT", "TOTAL");
        var vat = FindLabeledMoney(text, "TIỀN THUẾ GTGT", "THUẾ GTGT", "VAT AMOUNT", "VAT", "THUẾ");
        var subtotal = FindLabeledMoney(text, "CỘNG TIỀN HÀNG", "TIỀN HÀNG", "SUBTOTAL", "AMOUNT BEFORE TAX");

        // A common Vietnamese sales/POS invoice prints the row amount and the amount-in-words
        // even when the selectable PDF text layer is incomplete. Use those as evidence, not as guesses.
        if (total <= 0)
        {
            var amountWords = Regex.Match(text, @"(?i)SỐ TIỀN VIẾT BẰNG CHỮ[^:：]*[:：]?\s*([^\r\n]+)");
            if (amountWords.Success)
            {
                var words = amountWords.Groups[1].Value.ToLowerInvariant();
                var map = new Dictionary<string, decimal> { ["không"] = 0, ["một"] = 1, ["hai"] = 2, ["ba"] = 3, ["bốn"] = 4, ["tư"] = 4, ["năm"] = 5, ["lăm"] = 5, ["sáu"] = 6, ["bảy"] = 7, ["bẩy"] = 7, ["tám"] = 8, ["chín"] = 9 };
                decimal section = 0, result = 0, current = 0;
                foreach (var w in Regex.Split(words, @"\s+"))
                {
                    if (map.TryGetValue(w.Trim('.', ',', ';', '(', ')'), out var n)) current += n;
                    else if (w is "mười") current += 10;
                    else if (w is "trăm") current = (current == 0 ? 1 : current) * 100;
                    else if (w is "nghìn" or "ngàn") { result += (section + current) * 1_000; section = 0; current = 0; }
                    else if (w is "triệu") { result += (section + current) * 1_000_000; section = 0; current = 0; }
                    else if (w is "tỷ" or "tỉ") { result += (section + current) * 1_000_000_000; section = 0; current = 0; }
                }
                var wordTotal = result + section + current;
                if (wordTotal > 0) total = wordTotal;
            }
        }

        if (subtotal <= 0 && total > 0) subtotal = total - vat;

        if (string.IsNullOrWhiteSpace(invoiceNum)) { warnings.Add("Không tìm thấy số hóa đơn rõ ràng."); confidence -= 0.1m; }
        if (taxCode == null) { warnings.Add("Không tìm thấy mã số thuế."); confidence -= 0.15m; }
        if (invoiceDate == null) { warnings.Add("Không tìm thấy ngày hóa đơn."); confidence -= 0.1m; }
        if (total == 0) { warnings.Add("Không tìm thấy tổng tiền."); confidence -= 0.2m; }

        return new InvoiceExtractionResult
        {
            Provider = Name,
            SupplierName = supplier,
            SupplierTaxCode = taxCode,
            InvoiceNumber = invoiceNum,
            InvoiceDate = invoiceDate,
            Subtotal = subtotal,
            VatAmount = vat,
            Total = total,
            ConfidenceScore = Math.Max(0.20m, confidence),
            ProviderWarnings = warnings
        };
    }
}


public sealed class LocalAIProvider(HttpClient httpClient, IConfiguration configuration) : IAIProvider
{
    public string Name => "LocalAIProvider";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(configuration["AI_SERVICE_URL"]);

    public async Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        var baseUrl = (configuration["AI_SERVICE_URL"] ?? "http://ai-service:8000").TrimEnd('/');
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(input.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(input.ContentType);
        form.Add(content, "file", input.FileName);
        using var response = await httpClient.PostAsync($"{baseUrl}/ocr/analyze", form, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Local OCR service trả về lỗi {(int)response.StatusCode}: {json}");
        var parsed = JsonSerializer.Deserialize<LocalInvoiceResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        }) ?? throw new InvalidOperationException("Không thể phân tích phản hồi từ Local OCR service.");
        return new InvoiceExtractionResult
        {
            Provider = Name,
            PageCount = input.ContentType == "application/pdf" ? Math.Max(1, parsed.PageCount) : 1,
            SupplierName = parsed.SupplierName ?? string.Empty,
            SupplierTaxCode = parsed.SupplierTaxCode,
            InvoiceNumber = parsed.InvoiceNumber ?? string.Empty,
            Serial = parsed.Serial,
            InvoiceDate = DateTime.TryParse(parsed.InvoiceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            Subtotal = parsed.Subtotal,
            VatAmount = parsed.VatAmount,
            Total = parsed.Total,
            ConfidenceScore = Math.Clamp(parsed.ConfidenceScore, 0, 1),
            Items = parsed.Items ?? Array.Empty<ExtractedInvoiceItem>(),
            Evidence = parsed.Evidence ?? Array.Empty<OcrEvidence>(),
            ProviderWarnings = (parsed.ProviderWarnings ?? Array.Empty<string>())
                .Concat(new[] { $"Local OCR model: {parsed.ModelVersion ?? "unknown"}; pages: {parsed.PageCount}." })
                .ToArray()
        };
    }

    public async Task<(byte[] Bytes, string ContentType, int PageCount)> RenderPageAsync(InvoiceDocumentInput input, int page, CancellationToken cancellationToken)
    {
        var baseUrl = (configuration["AI_SERVICE_URL"] ?? "http://ai-service:8000").TrimEnd('/');
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(input.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(input.ContentType);
        form.Add(content, "file", input.FileName);
        form.Add(new StringContent(Math.Max(1, page).ToString(CultureInfo.InvariantCulture)), "page");
        form.Add(new StringContent("2"), "scale");
        using var response = await httpClient.PostAsync($"{baseUrl}/render", form, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Local OCR render service trả về lỗi {(int)response.StatusCode}.");
        var pageCount = 1;
        if (response.Headers.TryGetValues("X-Page-Count", out var values))
            int.TryParse(values.FirstOrDefault(), out pageCount);
        return (bytes, response.Content.Headers.ContentType?.MediaType ?? "image/png", Math.Max(1, pageCount));
    }

    public async Task<RegionOcrResult> AnalyzeRegionAsync(InvoiceDocumentInput input, OcrRegionRequest region, CancellationToken cancellationToken)
    {
        var baseUrl = (configuration["AI_SERVICE_URL"] ?? "http://ai-service:8000").TrimEnd('/');
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(input.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(input.ContentType);
        form.Add(content, "file", input.FileName);
        form.Add(new StringContent(region.Page.ToString(CultureInfo.InvariantCulture)), "page");
        form.Add(new StringContent(region.X.ToString(CultureInfo.InvariantCulture)), "x");
        form.Add(new StringContent(region.Y.ToString(CultureInfo.InvariantCulture)), "y");
        form.Add(new StringContent(region.Width.ToString(CultureInfo.InvariantCulture)), "width");
        form.Add(new StringContent(region.Height.ToString(CultureInfo.InvariantCulture)), "height");
        form.Add(new StringContent(region.Field ?? "region"), "field");
        using var response = await httpClient.PostAsync($"{baseUrl}/ocr/region", form, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Local region OCR trả về lỗi {(int)response.StatusCode}: {json}");
        return JsonSerializer.Deserialize<RegionOcrResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("Không thể phân tích kết quả OCR vùng.");
    }

    private sealed class LocalInvoiceResult
    {
        public string? SupplierName { get; init; }
        public string? SupplierTaxCode { get; init; }
        public string? InvoiceNumber { get; init; }
        public string? Serial { get; init; }
        public string? InvoiceDate { get; init; }
        public decimal Subtotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal Total { get; init; }
        public decimal ConfidenceScore { get; init; }
        public IReadOnlyList<ExtractedInvoiceItem>? Items { get; init; }
        public IReadOnlyList<OcrEvidence>? Evidence { get; init; }
        public IReadOnlyList<string>? ProviderWarnings { get; init; }
        public int PageCount { get; init; }
        public string? ModelVersion { get; init; }
    }
}

public sealed class HybridAIProvider(LocalAIProvider local, GeminiAIProvider gemini, IConfiguration configuration) : IAIProvider
{
    public string Name => "HybridAIProvider";
    public bool IsAvailable => local.IsAvailable || gemini.IsAvailable;

    public async Task<InvoiceExtractionResult> AnalyzeInvoiceAsync(InvoiceDocumentInput input, CancellationToken cancellationToken)
    {
        InvoiceExtractionResult? localResult = null;
        if (local.IsAvailable)
        {
            try { localResult = await local.AnalyzeInvoiceAsync(input, cancellationToken); }
            catch when (gemini.IsAvailable) { }
        }
        if (localResult is null)
            return gemini.IsAvailable
                ? await gemini.AnalyzeInvoiceAsync(input, cancellationToken)
                : throw new InvalidOperationException("Không có OCR provider khả dụng.");

        var threshold = decimal.TryParse(configuration["HYBRID_CONFIDENCE_THRESHOLD"], NumberStyles.Any, CultureInfo.InvariantCulture, out var t) ? t : 0.82m;
        // Accept either 0..1 or a human-friendly percentage such as 82.
        if (threshold > 1m && threshold <= 100m) threshold /= 100m;
        threshold = Math.Clamp(threshold, 0.50m, 0.98m);
        var localConfidence = Math.Clamp(localResult.ConfidenceScore, 0, 1);
        var missingCritical = string.IsNullOrWhiteSpace(localResult.SupplierName) || string.IsNullOrWhiteSpace(localResult.SupplierTaxCode)
                              || string.IsNullOrWhiteSpace(localResult.InvoiceNumber) || localResult.InvoiceDate is null || localResult.Total <= 0;
        var arithmeticMismatch = localResult.Total > 0 && Math.Abs((localResult.Subtotal + localResult.VatAmount) - localResult.Total) > 1m;
        var needsCloud = missingCritical || arithmeticMismatch || localConfidence < threshold;
        if (!needsCloud)
            return localResult.withProvider(Name, $"Hybrid: Local OCR đủ dữ liệu và đạt {localConfidence:P0} (ngưỡng {threshold:P0}), không cần Gemini.");
        if (!gemini.IsAvailable)
        {
            var reason = missingCritical ? "thiếu trường bắt buộc" : arithmeticMismatch ? "sai lệch tổng tiền" : $"confidence {localConfidence:P0} dưới ngưỡng {threshold:P0}";
            return localResult.withProvider(Name, $"Hybrid: Local OCR {reason}; Gemini chưa được cấu hình nên yêu cầu User Review.");
        }

        InvoiceExtractionResult cloud;
        try { cloud = await gemini.AnalyzeInvoiceAsync(input, cancellationToken); }
        catch (Exception ex) { return localResult.withProvider(Name, $"Hybrid: cần fallback Gemini nhưng Gemini lỗi ({ex.Message}); giữ Local OCR và yêu cầu User Review."); }

        static string Pick(string localValue, string cloudValue) => !string.IsNullOrWhiteSpace(localValue) ? localValue : cloudValue;
        static string? PickNullable(string? localValue, string? cloudValue) => !string.IsNullOrWhiteSpace(localValue) ? localValue : cloudValue;
        static decimal PickAmount(decimal localValue, decimal cloudValue) => localValue > 0 ? localValue : cloudValue;
        var localFinanciallyConsistent = localResult.Total > 0 && Math.Abs(localResult.Subtotal + localResult.VatAmount - localResult.Total) <= 1m;
        var useCloudCore = cloud.ConfidenceScore > localResult.ConfidenceScore + 0.12m && !localFinanciallyConsistent;
        return new InvoiceExtractionResult
        {
            Provider = Name,
            PageCount = Math.Max(localResult.PageCount, cloud.PageCount),
            SupplierName = useCloudCore ? Pick(cloud.SupplierName, localResult.SupplierName) : Pick(localResult.SupplierName, cloud.SupplierName),
            SupplierTaxCode = useCloudCore ? PickNullable(cloud.SupplierTaxCode, localResult.SupplierTaxCode) : PickNullable(localResult.SupplierTaxCode, cloud.SupplierTaxCode),
            InvoiceNumber = useCloudCore ? Pick(cloud.InvoiceNumber, localResult.InvoiceNumber) : Pick(localResult.InvoiceNumber, cloud.InvoiceNumber),
            Serial = useCloudCore ? PickNullable(cloud.Serial, localResult.Serial) : PickNullable(localResult.Serial, cloud.Serial),
            InvoiceDate = useCloudCore ? cloud.InvoiceDate ?? localResult.InvoiceDate : localResult.InvoiceDate ?? cloud.InvoiceDate,
            Subtotal = useCloudCore ? PickAmount(cloud.Subtotal, localResult.Subtotal) : PickAmount(localResult.Subtotal, cloud.Subtotal),
            VatAmount = useCloudCore ? PickAmount(cloud.VatAmount, localResult.VatAmount) : PickAmount(localResult.VatAmount, cloud.VatAmount),
            Total = useCloudCore ? PickAmount(cloud.Total, localResult.Total) : PickAmount(localResult.Total, cloud.Total),
            ConfidenceScore = Math.Clamp(Math.Max(localResult.ConfidenceScore, cloud.ConfidenceScore), 0, 1),
            Items = cloud.Items.Count > localResult.Items.Count ? cloud.Items : localResult.Items,
            Evidence = localResult.Evidence.Concat(cloud.Evidence).GroupBy(x => new { x.Field, x.Page }).Select(g => g.First()).ToArray(),
            ProviderWarnings = localResult.ProviderWarnings.Concat(cloud.ProviderWarnings)
                .Concat(new[] { $"Hybrid fusion: local={localResult.ConfidenceScore:P0}, gemini={cloud.ConfidenceScore:P0}; core={(useCloudCore ? "gemini" : "local")}." }).ToArray()
        };
    }
}

public static class InvoiceExtractionResultExtensions
{
    public static InvoiceExtractionResult withProvider(this InvoiceExtractionResult value, string provider, string warning) => new()
    {
        Provider = provider, PageCount = value.PageCount, SupplierName = value.SupplierName, SupplierTaxCode = value.SupplierTaxCode,
        InvoiceNumber = value.InvoiceNumber, Serial = value.Serial, InvoiceDate = value.InvoiceDate,
        Subtotal = value.Subtotal, VatAmount = value.VatAmount, Total = value.Total, ConfidenceScore = value.ConfidenceScore,
        Items = value.Items, Evidence = value.Evidence, ProviderWarnings = value.ProviderWarnings.Concat(new[] { warning }).ToArray()
    };
}

public sealed record RegionOcrResult(string Field, string Value, decimal Confidence, int Page, RegionBoundingBox BoundingBox, string Source);
public sealed record RegionBoundingBox(decimal X, decimal Y, decimal Width, decimal Height);

public sealed class AIProviderResolver(
    DemoAIProvider demo,
    OpenAIProvider openAi,
    GeminiAIProvider gemini,
    LocalAIProvider local,
    HybridAIProvider hybrid,
    PaddleOCRProvider paddle,
    TesseractOCRProvider tesseract,
    IConfiguration configuration)
{
    public IAIProvider Current
    {
        get
        {
            var selected = configuration["AI_PROVIDER"]?.Trim().ToLowerInvariant();
            return selected switch
            {
                "hybrid" when hybrid.IsAvailable => hybrid,
                "local" when local.IsAvailable => local,
                "openai" when openAi.IsAvailable => openAi,
                "gemini" when gemini.IsAvailable => gemini,
                "paddle" when paddle.IsAvailable => paddle,
                "tesseract" when tesseract.IsAvailable => tesseract,
                _ => fallback
            };
        }
    }

    private IAIProvider fallback =>
        hybrid.IsAvailable ? hybrid :
        local.IsAvailable ? local :
        gemini.IsAvailable ? gemini :
        paddle.IsAvailable ? paddle :
        openAi.IsAvailable ? openAi :
        tesseract.IsAvailable ? tesseract : demo;

    public bool FellBackToDemo => ReferenceEquals(Current, demo);
}


public sealed class SupplierMemoryService(AppDbContext db)
{
    public async Task<InvoiceExtractionResult> ApplyAsync(Guid companyId, InvoiceExtractionResult current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(current.SupplierTaxCode) && string.IsNullOrWhiteSpace(current.SupplierName)) return current;
        var recent = await db.OCRCorrections.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.CorrectedJson)
            .Take(250)
            .ToListAsync(ct);

        var candidates = new List<InvoiceExtractionResult>();
        foreach (var json in recent)
        {
            try
            {
                var value = JsonSerializer.Deserialize<InvoiceExtractionResult>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                });
                if (value is not null) candidates.Add(value);
            }
            catch { }
        }

        InvoiceExtractionResult? remembered = null;
        if (!string.IsNullOrWhiteSpace(current.SupplierTaxCode))
            remembered = candidates.FirstOrDefault(x => string.Equals(x.SupplierTaxCode, current.SupplierTaxCode, StringComparison.Ordinal));
        remembered ??= candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(current.SupplierName) &&
            string.Equals(x.SupplierName.Trim(), current.SupplierName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (remembered is null) return current;

        var filledName = string.IsNullOrWhiteSpace(current.SupplierName) || current.SupplierName.Contains("xác nhận", StringComparison.OrdinalIgnoreCase);
        var filledTax = string.IsNullOrWhiteSpace(current.SupplierTaxCode);
        if (!filledName && !filledTax) return current;
        return new InvoiceExtractionResult
        {
            Provider = current.Provider,
            PageCount = current.PageCount,
            SupplierName = filledName ? remembered.SupplierName : current.SupplierName,
            SupplierTaxCode = filledTax ? remembered.SupplierTaxCode : current.SupplierTaxCode,
            InvoiceNumber = current.InvoiceNumber,
            Serial = current.Serial,
            InvoiceDate = current.InvoiceDate,
            Subtotal = current.Subtotal,
            VatAmount = current.VatAmount,
            Total = current.Total,
            ConfidenceScore = current.ConfidenceScore,
            Items = current.Items,
            Evidence = current.Evidence,
            ProviderWarnings = current.ProviderWarnings.Concat(new[] { "Supplier memory: đã điền trường thiếu từ dữ liệu người dùng từng xác nhận; cần kiểm tra lại." }).ToArray()
        };
    }
}

public sealed class InvoiceValidationService(AppDbContext db)
{
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(
        Guid companyId,
        InvoiceExtractionResult result,
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(result.SupplierName) || result.SupplierName.Contains("xác nhận", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("supplierName", "MISSING", "Cần xác nhận tên nhà cung cấp.", "error"));
        if (string.IsNullOrWhiteSpace(result.SupplierTaxCode) || !Regex.IsMatch(result.SupplierTaxCode, @"^(\d{10}|\d{13})$"))
            issues.Add(new("supplierTaxCode", "INVALID_TAX_CODE", "MST phải có 10 hoặc 13 chữ số.", "error"));
        if (string.IsNullOrWhiteSpace(result.InvoiceNumber))
            issues.Add(new("invoiceNumber", "MISSING", "Thiếu số hóa đơn.", "error"));
        if (!result.InvoiceDate.HasValue)
            issues.Add(new("invoiceDate", "MISSING", "Thiếu ngày hóa đơn.", "error"));
        else if (result.InvoiceDate.Value.Date > DateTime.UtcNow.Date.AddDays(1))
            issues.Add(new("invoiceDate", "FUTURE_DATE", "Ngày hóa đơn nằm trong tương lai.", "error"));
        if (result.Total <= 0)
            issues.Add(new("total", "INVALID_TOTAL", "Tổng tiền phải lớn hơn 0.", "error"));
        if (result.Subtotal < 0 || result.VatAmount < 0)
            issues.Add(new("amount", "NEGATIVE_AMOUNT", "Tiền hàng và VAT không được âm.", "error"));
        if (result.Total > 0 && Math.Abs(result.Subtotal + result.VatAmount - result.Total) > 1m)
            issues.Add(new("total", "TOTAL_MISMATCH", "Tiền hàng + VAT không khớp tổng tiền.", "error"));
        if (result.Items is { Count: > 0 })
        {
            if (Math.Abs(result.Items.Sum(x => x.Total) - result.Total) > 1m)
                issues.Add(new("items", "LINE_TOTAL_MISMATCH", "Tổng thành tiền các dòng không khớp tổng hóa đơn.", "error"));
            for (var i = 0; i < result.Items.Count; i++)
            {
                var item = result.Items[i];
                if (string.IsNullOrWhiteSpace(item.Description))
                    issues.Add(new($"items[{i}].description", "MISSING", "Dòng hàng thiếu mô tả.", "error"));
                if (item.Quantity <= 0 || item.UnitPrice < 0 || item.VatAmount < 0 || item.Total < 0)
                    issues.Add(new($"items[{i}]", "INVALID_LINE", "Số lượng/đơn giá/tiền thuế của dòng hàng không hợp lệ.", "error"));
                if (Math.Abs(decimal.Round(item.Quantity * item.UnitPrice, 2) + item.VatAmount - item.Total) > 1m)
                    issues.Add(new($"items[{i}]", "LINE_ARITHMETIC", "Số lượng × đơn giá + VAT không khớp thành tiền.", "error"));
                if (item.VatRate is < 0 or > 100)
                    issues.Add(new($"items[{i}].vatRate", "INVALID_VAT_RATE", "Thuế suất dòng hàng phải từ 0 đến 100%.", "error"));
            }
        }
        if (!string.IsNullOrWhiteSpace(result.InvoiceNumber))
        {
            var duplicate = await db.Invoices.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.Direction == "INCOMING" &&
                x.Number == result.InvoiceNumber && x.PartyTaxCode == result.SupplierTaxCode,
                cancellationToken);
            if (duplicate)
                issues.Add(new("invoiceNumber", "DUPLICATE", "Hóa đơn có thể đã tồn tại.", "error"));
        }
        if (result.ConfidenceScore < 0.65m)
            issues.Add(new("confidenceScore", "LOW_CONFIDENCE", "Độ tin cậy OCR thấp; cần kiểm tra kỹ.", "warning"));
        return issues;
    }
}

public sealed record ValidationIssue(string Field, string Code, string Message, string Severity);

public interface IInvoiceProvider
{
    string Name { get; }
    Task<InvoiceIssueResult> IssueAsync(TaxFlow.Api.Models.Invoice invoice, CancellationToken cancellationToken);
}

public sealed record InvoiceIssueResult(bool IsLegallyIssued, string Status, string Message, string? ProviderReference);

public sealed class DemoInvoiceProvider : IInvoiceProvider
{
    public string Name => "DemoInvoiceProvider";
    public Task<InvoiceIssueResult> IssueAsync(TaxFlow.Api.Models.Invoice invoice, CancellationToken cancellationToken) =>
        Task.FromResult(new InvoiceIssueResult(
            false,
            TaxFlow.Api.Models.InvoiceStatuses.Pending,
            "Đã tạo yêu cầu mô phỏng. Chưa kết nối nhà cung cấp hóa đơn điện tử nên chưa phát hành hợp pháp.",
            null));
}
