using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>Yêu cầu tra cứu: mã người bệnh <b>hoặc</b> số CCCD (đúng một trong hai), gửi trong thân để không lộ trong URL/nhật ký.</summary>
public sealed record PatientLookupRequest(string? PatientCode, string? CitizenId);

/// <summary>Tra lượt tiếp nhận của một người bệnh (mã <b>hoặc</b> CCCD) từ ngày đến ngày (theo ngày của bệnh viện).</summary>
public sealed record EncounterSearchRequest(string? PatientCode, string? CitizenId, DateOnly? From, DateOnly? To);

/// <summary>Lấy số liệu trong ngày (theo ngày của bệnh viện) cho bảng tổng hợp điều dưỡng.</summary>
public sealed record DailyStatisticsRequest(DateOnly? Date);

/// <summary>
/// API thông tin người bệnh: dịch vụ riêng, chỉ đọc, là nơi duy nhất kết nối CSDL bệnh viện. Các phần mềm khác (phiếu đồng
/// thuận…) gọi qua HTTPS với khóa API, không cần giữ chuỗi kết nối CSDL.
/// </summary>
public static partial class PatientInfoProgram
{
    public static void Main(string[] args)
    {
        var app = Build(WebApplication.CreateBuilder(args));
        app.Run();
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<PatientInfoOptions>()
            .Bind(builder.Configuration.GetSection(PatientInfoOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Thiếu PatientInfo__ConnectionString (chuỗi kết nối CSDL bệnh viện, tài khoản chỉ đọc).")
            .Validate(o => o.HasValidQuery, "PatientInfo:Query phải là một câu SELECT (hoặc WITH … SELECT) dùng tham số @PatientCode và/hoặc @CitizenId, không có dấu ';' giữa câu.")
            .Validate(o => o.HasValidApiKeys, $"Thiếu khóa API: đặt PatientInfo__ApiKeys__0 (mỗi khóa ≥ {PatientInfoOptions.MinimumApiKeyLength} ký tự ngẫu nhiên).")
            .Validate(o => o.HasValidEncounterQuery,
                "PatientInfo:EncounterQuery (nếu đặt) phải là một câu SELECT dùng @From, @To và @PatientCode/@CitizenId, không có dấu ';' giữa câu.")
            .Validate(o => o.HasValidDailyStatisticsQuery,
                "PatientInfo:DailyStatisticsQuery (nếu đặt) phải là một câu SELECT dùng @From và @To, không có dấu ';' giữa câu.")
            .Validate(o => o.HasValidDepartmentQuery,
                "PatientInfo:DepartmentQuery (nếu đặt) phải là một câu SELECT (hoặc WITH … SELECT), không có dấu ';' giữa câu.")
            .Validate(o => o.QueryTimeoutSeconds is >= 1 and <= 60, "PatientInfo:QueryTimeoutSeconds phải trong khoảng 1–60 giây.")
            .Validate(o => o.HasUnambiguousGenderValues,
                "Giá trị giới tính bị trùng giữa Nam và Nữ: đặt cả PatientInfo__MaleValues__0… và PatientInfo__FemaleValues__0… (danh sách đặt thay hẳn mặc định).")
            .ValidateOnStart();
        builder.Services.AddSingleton<PatientRepository>();
        builder.Services.AddSingleton<EncounterRepository>();
        builder.Services.AddSingleton<DailyStatisticsRepository>();
        builder.Services.AddSingleton<DepartmentRepository>();
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseMiddleware<ApiKeyMiddleware>();
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
        app.MapPost("/api/v1/patients/lookup", LookupAsync);
        app.MapPost("/api/v1/encounters/search", SearchEncountersAsync);
        app.MapPost("/api/v1/daily-statistics", GetDailyStatisticsAsync);
        app.MapGet("/api/v1/departments", GetDepartmentsAsync);
        return app;
    }

    internal static async Task<IResult> LookupAsync(
        PatientLookupRequest? request,
        PatientRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var patientCode = string.IsNullOrWhiteSpace(request?.PatientCode) ? null : request.PatientCode.Trim();
        var citizenId = string.IsNullOrWhiteSpace(request?.CitizenId) ? null : request.CitizenId.Trim();
        if ((patientCode is null) == (citizenId is null))
        {
            return Problem("Nhập mã người bệnh hoặc số CCCD (một trong hai).");
        }

        if (patientCode is { Length: > 50 })
        {
            return Problem("Mã người bệnh tối đa 50 ký tự.");
        }

        if (citizenId is not null && !CitizenIdPattern().IsMatch(citizenId))
        {
            return Problem("Số CCCD gồm 12 chữ số.");
        }

        var logger = loggerFactory.CreateLogger("PatientInfo.Lookup");
        try
        {
            var patient = await repository.FindAsync(patientCode, citizenId, cancellationToken).ConfigureAwait(false);

            // Nhật ký không ghi mã người bệnh/CCCD hay thông tin trả về.
            logger.LogInformation("Tra cứu người bệnh theo {By}: {Result}", patientCode is null ? "citizenId" : "patientCode", patient is null ? "NotFound" : "Found");
            return patient is null ? Results.NotFound(new { title = "Không tìm thấy người bệnh." }) : Results.Ok(patient);
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Lỗi CSDL khi tra cứu người bệnh (mã lỗi SQL {Number}).", ex.Number);
            return Results.Problem("Không truy vấn được CSDL bệnh viện.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Lượt tiếp nhận nhiều nhất trả về một lần, và khoảng ngày dài nhất được tra.</summary>
    public const int MaxEncounters = 200;

    public const int MaxEncounterDays = 366;

    internal static async Task<IResult> SearchEncountersAsync(
        EncounterSearchRequest? request,
        EncounterRepository repository,
        IOptions<PatientInfoOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!options.Value.HasEncounterQuery)
        {
            return Results.Problem("Chưa cấu hình truy vấn lượt tiếp nhận (PatientInfo__EncounterQuery).", statusCode: StatusCodes.Status501NotImplemented);
        }

        var patientCode = string.IsNullOrWhiteSpace(request?.PatientCode) ? null : request.PatientCode.Trim();
        var citizenId = string.IsNullOrWhiteSpace(request?.CitizenId) ? null : request.CitizenId.Trim();
        if ((patientCode is null) == (citizenId is null))
        {
            return Problem("Nhập mã người bệnh hoặc số CCCD (một trong hai).");
        }

        if (patientCode is { Length: > 50 } || (citizenId is not null && !CitizenIdPattern().IsMatch(citizenId)))
        {
            return Problem("Mã người bệnh hoặc số CCCD không hợp lệ.");
        }

        if (request!.From is not { } from || request.To is not { } to || to < from || to.DayNumber - from.DayNumber >= MaxEncounterDays)
        {
            return Problem($"Khoảng ngày không hợp lệ (từ ngày ≤ đến ngày, tối đa {MaxEncounterDays} ngày).");
        }

        var logger = loggerFactory.CreateLogger("PatientInfo.Encounters");
        try
        {
            var items = await repository.SearchAsync(patientCode, citizenId, from, to, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Tra lượt tiếp nhận theo {By}: {Count} lượt", patientCode is null ? "citizenId" : "patientCode", items.Count);
            return Results.Ok(new { items });
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Lỗi CSDL khi tra lượt tiếp nhận (mã lỗi SQL {Number}).", ex.Number);
            return Results.Problem("Không truy vấn được CSDL bệnh viện.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Số dòng số liệu nhiều nhất trả về một lần.</summary>
    public const int MaxDailyStatistics = 1000;

    internal static async Task<IResult> GetDailyStatisticsAsync(
        DailyStatisticsRequest? request,
        DailyStatisticsRepository repository,
        IOptions<PatientInfoOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!options.Value.HasDailyStatisticsQuery)
        {
            return Results.Problem("Chưa cấu hình truy vấn số liệu trong ngày (PatientInfo__DailyStatisticsQuery).", statusCode: StatusCodes.Status501NotImplemented);
        }

        if (request?.Date is not { } date)
        {
            return Problem("Thiếu ngày lấy số liệu.");
        }

        var logger = loggerFactory.CreateLogger("PatientInfo.DailyStatistics");
        try
        {
            var items = await repository.GetAsync(date, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Lấy số liệu trong ngày: {Count} dòng", items.Count);
            return Results.Ok(new { items });
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Lỗi CSDL khi lấy số liệu trong ngày (mã lỗi SQL {Number}).", ex.Number);
            return Results.Problem("Không truy vấn được CSDL bệnh viện.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Số khoa/phòng nhiều nhất trả về một lần.</summary>
    public const int MaxDepartments = 2000;

    /// <summary>
    /// Danh mục khoa/phòng của HIS (quyết định 42). Kết quả dài quá giới hạn thì báo lỗi, không cắt bớt: phần mềm phiếu
    /// coi khoa/phòng không có trong danh mục là ngừng sử dụng.
    /// </summary>
    internal static async Task<IResult> GetDepartmentsAsync(
        DepartmentRepository repository,
        IOptions<PatientInfoOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!options.Value.HasDepartmentQuery)
        {
            return Results.Problem("Chưa cấu hình truy vấn danh mục khoa/phòng (PatientInfo__DepartmentQuery).", statusCode: StatusCodes.Status501NotImplemented);
        }

        var logger = loggerFactory.CreateLogger("PatientInfo.Departments");
        try
        {
            var items = await repository.ListAsync(MaxDepartments + 1, cancellationToken).ConfigureAwait(false);
            if (items.Count > MaxDepartments)
            {
                logger.LogWarning("Danh mục khoa/phòng có hơn {Max} dòng.", MaxDepartments);
                return Results.Problem(
                    $"Câu truy vấn danh mục khoa/phòng trả về hơn {MaxDepartments} dòng: chỉ lấy khoa/phòng đang hoạt động.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            logger.LogInformation("Lấy danh mục khoa/phòng: {Count} dòng", items.Count);
            return Results.Ok(new { items });
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Lỗi CSDL khi lấy danh mục khoa/phòng (mã lỗi SQL {Number}).", ex.Number);
            return Results.Problem("Không truy vấn được CSDL bệnh viện.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult Problem(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    [GeneratedRegex("^[0-9]{12}$")]
    private static partial Regex CitizenIdPattern();
}
