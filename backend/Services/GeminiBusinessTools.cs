using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;
public sealed record NaturalContext(string Kind, Guid CustomerId = default, Guid InvoiceId = default, string Metric = "", string FromDate = "", string ToDate = "");
public sealed record BusinessToolResult(string Answer, string Source, NaturalContext? Context = null);
public sealed class ToolInputException(string message) : Exception(message);

public sealed class GeminiBusinessTools(AppDbContext db, FinancialSummaryService financial, ReceivableConversationService debts)
{
    private static string Text(JsonElement args, string key, int maximum = 200)
    {
        if (!args.TryGetProperty(key,out var value) || value.ValueKind==JsonValueKind.Null) return "";
        if (value.ValueKind!=JsonValueKind.String) throw new ToolInputException("Tham số truy vấn chưa hợp lệ. Bạn vui lòng diễn đạt lại câu hỏi.");
        var text=value.GetString()?.Trim() ?? "";
        if(text.Length>maximum) throw new ToolInputException("Từ khóa quá dài. Bạn vui lòng nêu tên/mã hoặc kỳ cần xem ngắn gọn hơn.");
        return text;
    }
    private static bool Flag(JsonElement args,string key)
    {
        if(!args.TryGetProperty(key,out var value))return false;
        if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ToolInputException("Thông tin ngữ cảnh chưa hợp lệ. Bạn vui lòng hỏi lại rõ hơn.");
        return value.GetBoolean();
    }
    private static void ValidateFields(GeminiToolCall call)
    {
        var fields=call.Name switch {
            "get_receivables"=>new[]{"view","customerQuery","useContext"},
            "get_financial_metrics"=>new[]{"metric","fromDate","toDate","compareFromDate","compareToDate","useContext"},
            "get_invoices"=>new[]{"query","latest","direction","fromDate","toDate","useContext"},
            "get_inventory"=>new[]{"query","lowOnly"}, "get_alerts" or "find_customers"=>new[]{"query"},
            "explain_concept"=>new[]{"concept"}, "clarify_question"=>new[]{"question"}, _=>Array.Empty<string>() };
        if(!GeminiFunctionPlanner.AllowedFunctions.Contains(call.Name)||call.Arguments.EnumerateObject().Any(x=>!fields.Contains(x.Name)))
            throw new ToolInputException("Công cụ hoặc tham số này chưa được hỗ trợ. Mình chỉ có thể đọc dữ liệu nghiệp vụ được cấp quyền.");
    }
    public async Task<BusinessToolResult> ExecuteAsync(Guid cid,Guid uid,GeminiToolCall call,NaturalContext? context,CancellationToken ct)
    {
        ValidateFields(call);var args=call.Arguments;
        return call.Name switch {
            "get_receivables"=>await ReceivablesAsync(cid,uid,args,context,ct),
            "get_financial_metrics"=>await MetricsAsync(cid,args,context,ct),
            "get_invoices"=>await InvoicesAsync(cid,args,context,ct),
            "get_inventory"=>await InventoryAsync(cid,args,ct),
            "get_alerts"=>await AlertsAsync(cid,args,ct),
            "find_customers"=>await CustomersAsync(cid,args,ct),
            "explain_concept"=>Explain(Text(args,"concept",40)),
            "clarify_question"=>Clarify(Text(args,"question",400)),
            _=>throw new ToolInputException("Chưa có công cụ phù hợp với câu hỏi này.") };
    }
    private async Task<BusinessToolResult> ReceivablesAsync(Guid cid,Guid uid,JsonElement args,NaturalContext? context,CancellationToken ct)
    {
        var view=Text(args,"view",30);if(!new[]{"top","summary","details","overdue","payments"}.Contains(view))throw new ToolInputException("Bạn muốn xem tổng công nợ, khách nợ nhiều nhất, khoản quá hạn hay lịch sử thanh toán?");
        var query=Text(args,"customerQuery");var useContext=Flag(args,"useContext");Guid? customerId=null;
        if(query.Length>0)
        {
            var names=await db.Customers.AsNoTracking().Where(x=>x.CompanyId==cid).Select(x=>new{x.Id,x.Name,x.Code}).ToListAsync(ct);
            var q=AssistantPeriod.Normalize(query);
            var exact=names.Where(x=>AssistantPeriod.Normalize(x.Name)==q||AssistantPeriod.Normalize(x.Code)==q).ToList();
            var matches=exact.Count>0?exact:names.Where(x=>AssistantPeriod.Normalize(x.Name).Contains(q)||AssistantPeriod.Normalize(x.Code).Contains(q)).ToList();
            if(matches.Count!=1)return new(matches.Count==0?"Không tìm thấy khách hàng khớp tên/mã trong doanh nghiệp đang chọn.":"Có nhiều khách hàng khớp. Bạn vui lòng chọn rõ tên hoặc mã:\n"+string.Join("\n",matches.Take(10).Select(x=>$"• {x.Code} — {x.Name}")),"Danh mục khách hàng");
            customerId=matches[0].Id;
        }
        else if(useContext)
        {
            if(context?.Kind!="receivable"||context.CustomerId==Guid.Empty)return new("Bạn đang hỏi khoản nợ của khách hàng nào? Hãy nêu tên/mã hoặc hỏi ai đang nợ nhiều nhất trước.","Ngữ cảnh hội thoại");
            customerId=context.CustomerId;
        }
        if(view=="summary"&&!customerId.HasValue)
        {
            var rows=await db.Receivables.AsNoTracking().Where(x=>x.CompanyId==cid&&x.RemainingAmount>0&&x.Status!="CANCELLED").Select(x=>new{x.RemainingAmount,x.DueDate}).ToListAsync(ct);
            return new($"Hiện có {rows.Count} khoản phải thu còn dư, tổng {Money(rows.Sum(x=>x.RemainingAmount))}. Số dư quá hạn: {Money(rows.Where(x=>x.DueDate.Date<DateTime.UtcNow.Date).Sum(x=>x.RemainingAmount))}. Muốn xem chi tiết, bạn hãy nêu khách hàng hoặc hỏi ai nợ nhiều nhất.","Công nợ phải thu hiện tại");
        }
        if(view!="top"&&!customerId.HasValue)return new("Bạn muốn xem khoản nợ của khách hàng nào?", "Ngữ cảnh hội thoại");
        var canonical=view switch{"top"=>"khách hàng nợ nhiều nhất","overdue"=>"quá hạn bao lâu","payments"=>"họ đã trả chưa",_=>"khoản gì"};
        var reply=await debts.TryAnswerAsync(cid,uid,canonical,ct,customerId,context?.Kind=="receivable"?new ConversationContext("receivable",context.CustomerId):null);
        return reply is null?new("Mình chưa xác định được khoản nợ cần xem.","Ngữ cảnh hội thoại"):new(reply.Answer,reply.Source,reply.Context is null?null:new NaturalContext("receivable",reply.Context.CustomerId));
    }
    private sealed record DateRange(DateTime FromUtc,DateTime ToExclusiveUtc,string From,string To)
    {public string Label=>$"{DateTime.ParseExact(From,"yyyy-MM-dd",CultureInfo.InvariantCulture):dd/MM/yyyy}–{DateTime.ParseExact(To,"yyyy-MM-dd",CultureInfo.InvariantCulture):dd/MM/yyyy} (giờ Việt Nam)";}
    private static DateRange Range(string from,string to)
    {
        if(!DateTime.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start)||!DateTime.TryParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var end)||end<start||start.Year<2000||end.Year>2200||(end-start).TotalDays>1830)
            throw new ToolInputException("Bạn vui lòng nêu khoảng ngày hợp lệ, tối đa 5 năm cho mỗi truy vấn.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        return new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start,DateTimeKind.Unspecified),zone),TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end.AddDays(1),DateTimeKind.Unspecified),zone),from,to);
    }
    private async Task<decimal> SumAsync(IQueryable<decimal> query,CancellationToken ct)=>db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"?(await query.ToListAsync(ct)).Sum():await query.SumAsync(ct);
    private async Task<Dictionary<string,decimal>> MetricsForAsync(Guid cid,DateRange range,CancellationToken ct)
    {
        var p=await financial.ProfitAsync(cid,range.FromUtc,range.ToExclusiveUtc,ct);
        var incoming=await SumAsync(db.IncomeTransactions.Where(x=>x.CompanyId==cid&&x.TransactionDate>=range.FromUtc&&x.TransactionDate<range.ToExclusiveUtc).Select(x=>x.Amount),ct);
        var outgoing=await SumAsync(db.ExpenseTransactions.Where(x=>x.CompanyId==cid&&x.TransactionDate>=range.FromUtc&&x.TransactionDate<range.ToExclusiveUtc).Select(x=>x.Amount),ct);
        var vin=await SumAsync(db.Invoices.Where(x=>x.CompanyId==cid&&x.Direction=="INCOMING"&&x.AccountingReviewed&&x.Status!=InvoiceStatuses.Cancelled&&x.InvoiceDate>=range.FromUtc&&x.InvoiceDate<range.ToExclusiveUtc).Select(x=>x.VatAmount),ct);
        var vout=await SumAsync(db.Invoices.Where(x=>x.CompanyId==cid&&x.Direction=="OUTGOING"&&x.IsLegallyIssued&&x.Status!=InvoiceStatuses.Cancelled&&x.InvoiceDate>=range.FromUtc&&x.InvoiceDate<range.ToExclusiveUtc).Select(x=>x.VatAmount),ct);
        return new(){["revenue"]=p.NetRevenue,["income"]=incoming,["expense"]=outgoing,["cashflow"]=incoming-outgoing,["profit"]=p.EstimatedProfit,["vat"]=vout-vin,["vatInput"]=vin,["vatOutput"]=vout,["costOfGoods"]=p.CostOfGoods,["operatingExpense"]=p.OperatingExpense,["operating_expense"]=p.OperatingExpense,["cost_of_goods"]=p.CostOfGoods};
    }
    private async Task<BusinessToolResult> MetricsAsync(Guid cid,JsonElement args,NaturalContext? context,CancellationToken ct)
    {
        var metric=Text(args,"metric",30);
        var titles=new Dictionary<string,string>{{"revenue","Doanh thu chưa VAT"},{"income","Tiền vào"},{"expense","Tiền ra"},{"operating_expense","Chi phí vận hành chưa VAT"},{"cost_of_goods","Giá vốn hàng đã bán"},{"cashflow","Dòng tiền ròng"},{"profit","Lợi nhuận quản trị ước tính"},{"vat","Chênh lệch VAT đã rà soát/phát hành"}};
        if(metric is "receivables" or "payables")
        {
            var value=metric=="receivables"?await SumAsync(db.Receivables.Where(x=>x.CompanyId==cid&&x.RemainingAmount>0&&x.Status!="CANCELLED").Select(x=>x.RemainingAmount),ct):await SumAsync(db.Payables.Where(x=>x.CompanyId==cid&&x.RemainingAmount>0&&x.Status!="CANCELLED").Select(x=>x.RemainingAmount),ct);
            return new($"Tổng {(metric=="receivables"?"phải thu":"phải trả")} hiện tại: {Money(value)}. Chưa có dữ liệu số dư lịch sử để khẳng định số dư tại ngày quá khứ.","Sổ công nợ hiện tại");
        }
        if(!titles.ContainsKey(metric)&&metric!="summary")throw new ToolInputException("Bạn muốn xem doanh thu, lợi nhuận, dòng tiền, tiền vào/ra hay VAT?");
        var from=Text(args,"fromDate",10);var to=Text(args,"toDate",10);
        if(Flag(args,"useContext")&&context?.Kind=="finance"){if(from.Length==0)from=context.FromDate;if(to.Length==0)to=context.ToDate;}
        if(from.Length==0||to.Length==0)return new("Bạn muốn xem chỉ tiêu này trong kỳ nào? Có thể nói tháng này, quý trước, năm ngoái hoặc nêu khoảng ngày.","Cần xác định kỳ thời gian");
        var range=Range(from,to);var values=await MetricsForAsync(cid,range,ct);
        string Render(Dictionary<string,decimal> v)=>metric=="summary"?string.Join("; ",titles.Select(x=>$"{x.Value}: {Money(v[x.Key])}")):$"{titles[metric]}: {Money(v[metric])}";
        var answer=$"Kỳ {range.Label}:\n{Render(values)}.";
        if(metric=="profit")answer+=$"\nCông thức: doanh thu chưa VAT {Money(values["revenue"])} − giá vốn {Money(values["costOfGoods"])} − chi phí vận hành chưa VAT {Money(values["operatingExpense"])}.";
        if(metric=="vat")answer+=$"\nVAT đầu vào đã rà soát {Money(values["vatInput"])}, đầu ra được xác nhận phát hành {Money(values["vatOutput"])}; chưa phải tờ khai chính thức.";
        var cf=Text(args,"compareFromDate",10);var ctDate=Text(args,"compareToDate",10);
        if(cf.Length>0||ctDate.Length>0)
        {
            if(cf.Length==0||ctDate.Length==0)throw new ToolInputException("Bạn vui lòng nêu đầy đủ hai kỳ muốn so sánh.");
            var other=Range(cf,ctDate);var previous=await MetricsForAsync(cid,other,ct);
            answer+=$"\n\nKỳ đối chiếu {other.Label}:\n{Render(previous)}.";
            if(metric!="summary")answer+=$"\nChênh lệch kỳ đầu so với kỳ đối chiếu: {Money(values[metric]-previous[metric])}.";
            answer+="\nSố liệu cho biết chênh lệch; chưa đủ để tự kết luận nguyên nhân tăng/giảm.";
        }
        return new(answer,"Đơn hàng, giá vốn, thu/chi và hóa đơn trong database",new NaturalContext("finance",Metric:metric,FromDate:from,ToDate:to));
    }
    private async Task<BusinessToolResult> InvoicesAsync(Guid cid,JsonElement args,NaturalContext? context,CancellationToken ct)
    {
        var query=db.Invoices.AsNoTracking().Where(x=>x.CompanyId==cid);
        var search=Text(args,"query");var direction=Text(args,"direction",10);
        if(direction.Length>0&&direction!="ALL"){if(direction is not ("INCOMING" or "OUTGOING"))throw new ToolInputException("Loại hóa đơn chưa hợp lệ.");query=query.Where(x=>x.Direction==direction);}
        if(Flag(args,"useContext")){if(context?.Kind!="invoice"||context.InvoiceId==Guid.Empty)return new("Bạn đang hỏi hóa đơn nào? Hãy nêu số hóa đơn hoặc hỏi hóa đơn mới nhất.","Ngữ cảnh hội thoại");query=query.Where(x=>x.Id==context.InvoiceId);}
        if(search.Length>0)query=query.Where(x=>x.Number.Contains(search)||x.PartyName.Contains(search));
        var from=Text(args,"fromDate",10);var to=Text(args,"toDate",10);
        if(from.Length>0||to.Length>0){var range=Range(from,to);query=query.Where(x=>x.InvoiceDate>=range.FromUtc&&x.InvoiceDate<range.ToExclusiveUtc);}
        var count=await query.CountAsync(ct);var latest=Flag(args,"latest");
        var rows=await query.Include(x=>x.Items).OrderByDescending(x=>x.InvoiceDate).ThenBy(x=>x.Id).Take(latest?1:20).ToListAsync(ct);
        if(rows.Count==0)return new("Không có hóa đơn phù hợp trong doanh nghiệp đang chọn.","Sổ hóa đơn");
        var answer=string.Join("\n",rows.Select(x=>$"• HĐ {x.Number} — {x.PartyName} — ngày {x.InvoiceDate:dd/MM/yyyy}: tiền hàng {Money(x.Subtotal)}, VAT {Money(x.VatAmount)}, tổng {Money(x.Total)}, trạng thái {x.Status}."));
        if(rows.Count==1&&rows[0].Items.Count>0)answer+="\nChi tiết:\n"+string.Join("\n",rows[0].Items.Take(30).Select(x=>$"• {x.Description}: {x.Quantity} {x.Unit} × {Money(x.UnitPrice)}, thành tiền gồm VAT {Money(x.Total)}."));
        if(!latest&&count>20)answer+=$"\nHiển thị 20/{count} hóa đơn; hãy thu hẹp kỳ hoặc từ khóa để xem thêm.";
        return new(answer,"Hóa đơn và chi tiết hóa đơn",rows.Count==1?new NaturalContext("invoice",InvoiceId:rows[0].Id):null);
    }
    private async Task<BusinessToolResult> InventoryAsync(Guid cid,JsonElement args,CancellationToken ct)
    {
        var query=db.Products.AsNoTracking().Where(x=>x.CompanyId==cid&&x.IsActive);var search=Text(args,"query");
        if(search.Length>0)query=query.Where(x=>x.Name.Contains(search)||x.Sku.Contains(search)||(x.Barcode!=null&&x.Barcode.Contains(search)));
        if(Flag(args,"lowOnly"))query=query.Where(x=>x.Stock<=x.MinimumStock);
        var count=await query.CountAsync(ct);var rows=await query.OrderBy(x=>x.Stock).Take(20).ToListAsync(ct);
        return new(rows.Count==0?"Không tìm thấy sản phẩm phù hợp.":string.Join("\n",rows.Select(x=>$"• {x.Sku} — {x.Name}: còn {x.Stock} {x.Unit}, ngưỡng tối thiểu {x.MinimumStock}, giá bán {Money(x.SellingPrice)}."))+(count>20?$"\nHiển thị 20/{count} sản phẩm.":""),"Tồn kho hiện tại");
    }
    private async Task<BusinessToolResult> AlertsAsync(Guid cid,JsonElement args,CancellationToken ct)
    {
        var query=db.AIAlerts.AsNoTracking().Where(x=>x.CompanyId==cid);var search=Text(args,"query");
        if(search.Length>0)query=query.Where(x=>x.Title.Contains(search)||x.Message.Contains(search));
        var count=await query.CountAsync(ct);var rows=await query.OrderByDescending(x=>x.CreatedAt).Take(20).ToListAsync(ct);
        return new(rows.Count==0?"Chưa có cảnh báo phù hợp; điều này không chứng minh mọi giao dịch đều bình thường.":string.Join("\n",rows.Select(x=>$"• [{x.Severity}] {x.Title}: {x.Message}"))+(count>20?$"\nHiển thị 20/{count} cảnh báo.":"")+"\nCảnh báo cần được kiểm tra, chưa phải kết luận gian lận.","Cảnh báo đã lưu");
    }
    private async Task<BusinessToolResult> CustomersAsync(Guid cid,JsonElement args,CancellationToken ct)
    {
        var search=Text(args,"query");if(search.Length==0)throw new ToolInputException("Bạn muốn tìm khách hàng theo tên hoặc mã nào?");
        var rows=await db.Customers.AsNoTracking().Where(x=>x.CompanyId==cid&&(x.Name.Contains(search)||x.Code.Contains(search))).OrderBy(x=>x.Name).Take(20).ToListAsync(ct);
        return new(rows.Count==0?"Không tìm thấy khách hàng phù hợp.":string.Join("\n",rows.Select(x=>$"• {x.Code} — {x.Name}; MST {x.TaxCode??"chưa có"}.")),"Danh mục khách hàng",rows.Count==1?new NaturalContext("receivable",rows[0].Id):null);
    }
    private static BusinessToolResult Clarify(string text)
    {
        var q=AssistantPeriod.Normalize(text);
        if(text.Length==0||text.Any(char.IsDigit)||new[]{"mat khau","api key","token","secret","prompt"}.Any(q.Contains))text="Bạn vui lòng nêu rõ đối tượng, chỉ tiêu hoặc kỳ thời gian muốn xem.";
        return new(text,"Cần làm rõ yêu cầu");
    }
    private static BusinessToolResult Explain(string concept)=>new(concept switch{
        "greeting"=>"Chào bạn! Bạn có thể hỏi tự nhiên về doanh thu, dòng tiền, khách hàng còn nợ, hóa đơn hoặc tồn kho của doanh nghiệp đang chọn.",
        "revenue"=>"Trong TaxFlow, doanh thu quản trị là tổng giá trị đơn hoàn tất sau giảm giá, chưa gồm VAT. Doanh thu khác với số tiền thực tế đã thu.",
        "profit"=>"Lợi nhuận quản trị ước tính được tính từ doanh thu chưa VAT trừ giá vốn hàng đã bán và chi phí vận hành chưa VAT. Đây chưa phải kết quả kế toán đã kiểm toán.",
        "receivables"=>"Phải thu là số tiền khách hàng còn nợ doanh nghiệp. Có thể hỏi ai nợ nhiều nhất, khoản nào quá hạn hoặc lịch sử thanh toán của một khách hàng.",
        "payables"=>"Phải trả là số tiền doanh nghiệp còn phải thanh toán cho nhà cung cấp. Đây khác với tiền đã chi trong kỳ.",
        "vat"=>"Trong báo cáo này, VAT đầu vào chỉ gồm hóa đơn đã rà soát nội bộ; đầu ra chỉ gồm hóa đơn được xác nhận phát hành. Số liệu chưa phải tờ khai thuế chính thức.",
        _=>"Mình có thể đọc công nợ, thanh toán, doanh thu/lợi nhuận/dòng tiền theo kỳ, so sánh kỳ, hóa đơn, tồn kho và cảnh báo. Khi thiếu đối tượng hoặc thời gian, mình sẽ hỏi lại. Mình không tự chuyển tiền, sửa hoặc xóa dữ liệu."},"Giải thích cách TaxFlow hoạt động");
    private static string Money(decimal value)=>value.ToString("N0",CultureInfo.GetCultureInfo("vi-VN"))+" ₫";
}
