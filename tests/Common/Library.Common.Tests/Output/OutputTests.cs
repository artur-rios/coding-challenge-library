using Library.Output;
using Library.Testing.Attributes;

namespace Library.Common.Tests.Output;

// Unit tests for the response envelopes: ProcessOutput, DataOutput and PaginatedOutput.
public class OutputTests
{
    [UnitFact]
    public void GivenNoErrors_WhenAsked_ThenTheOutputSucceeds()
    {
        // Given
        var output = ProcessOutput.New.WithMessage("Done");

        // When
        var success = output.Success;

        // Then
        Assert.True(success);
        Assert.Equal(["Done"], output.Messages);
    }

    [UnitFact]
    public void GivenAnError_WhenAsked_ThenTheOutputFails()
    {
        // Given
        var output = ProcessOutput.New.WithError("Broken");

        // When
        var success = output.Success;

        // Then
        Assert.False(success);
        Assert.Equal(["Broken"], output.Errors);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenABlankErrorOrMessage_WhenAdded_ThenItIsIgnored(string blank)
    {
        // Given
        var output = ProcessOutput.New;

        // When
        output.AddError(blank);
        output.AddMessage(blank);

        // Then
        Assert.True(output.Success);
        Assert.Empty(output.Errors);
        Assert.Empty(output.Messages);
    }

    [UnitFact]
    public void GivenSeveralErrorsAndMessagesAmongBlanks_WhenAdded_ThenOnlyTheNonBlankOnesAreKeptInOrder()
    {
        // Given
        var output = ProcessOutput.New;

        // When
        output.WithErrors(["first", "", "second", "  "]).WithMessages([" ", "note", "other"]);

        // Then
        Assert.Equal(["first", "second"], output.Errors);
        Assert.Equal(["note", "other"], output.Messages);
    }

    [UnitFact]
    public void GivenNullLists_WhenAdded_ThenNothingChanges()
    {
        // Given
        var output = ProcessOutput.New.WithError("kept").WithMessage("kept");

        // When
        output.AddErrors(null);
        output.AddMessages(null);

        // Then
        Assert.Equal(["kept"], output.Errors);
        Assert.Equal(["kept"], output.Messages);
    }

    [UnitFact]
    public void GivenNullAssignedToTheLists_WhenRead_ThenTheyAreEmptyRatherThanNull()
    {
        // Given
        var output = ProcessOutput.New.WithError("gone").WithMessage("gone");

        // When
        output.Errors = null!;
        output.Messages = null!;

        // Then
        Assert.Empty(output.Errors);
        Assert.Empty(output.Messages);
        Assert.True(output.Success);
    }

    [UnitFact]
    public void GivenANewOutput_WhenCreated_ThenItIsStampedWithTheCurrentUtcTime()
    {
        // Given
        var before = DateTime.UtcNow;

        // When
        var output = ProcessOutput.New;

        // Then
        Assert.InRange(output.Timestamp, before, DateTime.UtcNow);
    }

    [UnitFact]
    public void GivenADataOutput_WhenBuiltFluently_ThenItKeepsItsTypeAndEverythingAdded()
    {
        // When
        var output = DataOutput<string>.New
            .WithData("payload")
            .WithMessage("message")
            .WithMessages(["more"])
            .WithError("error")
            .WithErrors(["another"]);

        // Then
        Assert.Equal("payload", output.Data);
        Assert.Equal(["message", "more"], output.Messages);
        Assert.Equal(["error", "another"], output.Errors);
        Assert.False(output.Success);
    }

    [UnitFact]
    public void GivenADataOutput_WhenDataIsAdded_ThenItReplacesTheData()
    {
        // Given
        var output = DataOutput<int>.New.WithData(1);

        // When
        output.AddData(2);

        // Then
        Assert.Equal(2, output.Data);
    }

    [UnitTheory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 5, 5)]
    public void GivenTotalItemsAndAPageSize_WhenCountingPages_ThenThePagesRoundUp(int totalItems, int pageSize, int pages)
    {
        // Given
        var output = PaginatedOutput<int>.New.WithPagination(1, pageSize, totalItems);

        // When
        var totalPages = output.TotalPages;

        // Then
        Assert.Equal(pages, totalPages);
    }

    [UnitFact]
    public void GivenAPaginatedOutput_WhenBuiltFluently_ThenItKeepsItsTypeAndEverythingAdded()
    {
        // When
        var output = PaginatedOutput<int>.New
            .WithData([1, 2])
            .WithPagination(2, 2, 6)
            .WithMessage("page")
            .WithMessages(["more"])
            .WithError("error")
            .WithErrors(["another"]);

        // Then
        Assert.Equal([1, 2], output.Data);
        Assert.Equal(2, output.PageNumber);
        Assert.Equal(2, output.PageSize);
        Assert.Equal(6, output.TotalItems);
        Assert.Equal(3, output.TotalPages);
        Assert.Equal(["page", "more"], output.Messages);
        Assert.Equal(["error", "another"], output.Errors);
    }

    [UnitFact]
    public void GivenAPaginatedOutput_WhenGivenEmptyData_ThenItHoldsAnEmptyListRatherThanNull()
    {
        // When
        var output = PaginatedOutput<int>.New.WithEmptyData();

        // Then
        Assert.NotNull(output.Data);
        Assert.Empty(output.Data);
    }
}
