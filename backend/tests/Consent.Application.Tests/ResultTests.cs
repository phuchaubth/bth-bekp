using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var error = Error.Validation("cccd.invalid", "Số CCCD không hợp lệ.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ReadingValue_Throws()
    {
        var result = Result<int>.Failure(Error.NotFound("patient.not_found", "Không tìm thấy người bệnh."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_WithNullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(5, 0, 0)]
    public void PagedResult_ComputesTotalPages(int totalCount, int pageSize, int expectedPages)
    {
        var page = new PagedResult<string>([], 1, pageSize, totalCount);

        Assert.Equal(expectedPages, page.TotalPages);
    }
}
