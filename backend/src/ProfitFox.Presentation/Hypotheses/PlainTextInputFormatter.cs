using System.Text;

using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace ProfitFox.Presentation.Hypotheses;

/// <summary>
///     Reads a <c>text/plain</c> body as the string it holds, byte for byte as decoded: the hash of a hypothesis is the hash of
///     the text that was sent, not of a JSON that was built again.
/// </summary>
public sealed class PlainTextInputFormatter : TextInputFormatter
{
    public PlainTextInputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/plain"));
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanReadType(Type type)
    {
        return type == typeof(string);
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
    {
        using var reader = new StreamReader(context.HttpContext.Request.Body, encoding);
        return await InputFormatterResult.SuccessAsync(await reader.ReadToEndAsync(context.HttpContext.RequestAborted));
    }
}
