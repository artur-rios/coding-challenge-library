using FluentValidation;
using Library.Data.Relational.Interfaces;
using Library.Domain.Entities;
using Library.Mediator.Query.Interfaces;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Query.Handlers;

/// <summary>
///     Reads the business audit trail. Never cached: the trail is what somebody consults when they
///     have stopped trusting the rest of the system, and an answer that might be a minute old is not
///     the answer they came for.
/// </summary>
public class ListAuditEventsQueryHandler(
    IValidator<ListAuditEventsQuery> validator,
    IAsyncReadOnlyRepository<AuditEvent> auditReader)
    : IPaginatedQueryHandlerAsync<ListAuditEventsQuery, AuditEventOutput>
{
    public async Task<PaginatedOutput<AuditEventOutput>> HandleAsync(
        ListAuditEventsQuery query, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(query, ct);

        if (!validation.IsValid)
        {
            return PaginatedOutput<AuditEventOutput>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var events = auditReader.Query().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim().ToLower();
            events = events.Where(entry => entry.EntityType == entityType);
        }

        if (query.EntityId is { } entityId)
        {
            events = events.Where(entry => entry.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim().ToLower();
            events = events.Where(entry => entry.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            var actor = query.Actor.Trim().ToLower();
            events = events.Where(entry => entry.Actor.ToLower() == actor);
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            var correlationId = query.CorrelationId.Trim();
            events = events.Where(entry => entry.CorrelationId == correlationId);
        }

        if (query.From is { } from)
        {
            events = events.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(entry => entry.OccurredAt <= to);
        }

        // Materialised as the stored shape first, because the JSON column is parsed in memory - a
        // projection that called ParseData inside the expression tree could not be translated to SQL.
        var page = await events
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new
            {
                entry.PublicId,
                entry.EntityType,
                entry.EntityId,
                entry.Action,
                entry.Actor,
                entry.OccurredAt,
                entry.CorrelationId,
                entry.Data
            })
            .PaginateAsync(query.PageNumber, query.PageSize, ct);

        var entries = (page.Data ?? []).Select(entry => new AuditEventOutput
        {
            Id = entry.PublicId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            Actor = entry.Actor,
            OccurredAt = entry.OccurredAt,
            CorrelationId = entry.CorrelationId,
            Data = AuditEventOutput.ParseData(entry.Data)
        }).ToList();

        return PaginatedOutput<AuditEventOutput>.New
            .WithData(entries)
            .WithPagination(page.PageNumber, page.PageSize, page.TotalItems)
            .WithMessage(AuditMessages.AuditEventsRetrievedSuccessfully);
    }
}
