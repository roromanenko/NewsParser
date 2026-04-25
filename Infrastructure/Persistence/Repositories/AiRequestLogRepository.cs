using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class AiRequestLogRepository(IDbConnectionFactory factory, IUnitOfWork uow) : IAiRequestLogRepository
{
	public async Task AddAsync(AiRequestLog entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		var entity = entry.ToEntity();
		await using var conn = await factory.CreateOpenAsync(cancellationToken);
		await conn.ExecuteAsync(new CommandDefinition(
			AiRequestLogSql.Insert,
			entity,
			cancellationToken: cancellationToken));
	}

	public async Task<AiRequestLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await using var conn = await factory.CreateOpenAsync(cancellationToken);
		var entity = await conn.QuerySingleOrDefaultAsync<AiRequestLogEntity>(
			new CommandDefinition(AiRequestLogSql.GetById, new { id }, cancellationToken: cancellationToken));
		return entity?.ToDomain();
	}

	public async Task<List<AiRequestLog>> GetPagedAsync(
		AiRequestLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
	{
		var (whereClause, parameters) = BuildWhere(filter);
		AppendStatusClause(filter, ref whereClause, parameters);
		AppendSearchClause(filter, ref whereClause, parameters);

		var offset = (page - 1) * pageSize;
		parameters.Add("pageSize", pageSize);
		parameters.Add("offset", offset);

		var sql = string.Format(AiRequestLogSql.GetPaged, whereClause);

		await using var conn = await factory.CreateOpenAsync(cancellationToken);
		var entities = await conn.QueryAsync<AiRequestLogEntity>(
			new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<int> CountAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default)
	{
		var (whereClause, parameters) = BuildWhere(filter);
		AppendStatusClause(filter, ref whereClause, parameters);
		AppendSearchClause(filter, ref whereClause, parameters);

		var sql = string.Format(AiRequestLogSql.Count, whereClause);

		await using var conn = await factory.CreateOpenAsync(cancellationToken);
		return await conn.ExecuteScalarAsync<int>(
			new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
	}

	public async Task<AiRequestLogMetrics> GetMetricsAsync(
		AiRequestLogFilter filter, CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildWhere(filter);
		var sql = string.Format(AiRequestLogSql.Metrics, where);

		await using var conn = await factory.CreateOpenAsync(cancellationToken);
		using var grid = await conn.QueryMultipleAsync(
			new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

		var totalsRow = await grid.ReadSingleAsync<AiMetricsTotalsRow>();
		var timeSeries = (await grid.ReadAsync<AiMetricsTimeBucketRow>()).ToList();
		var byModel = (await grid.ReadAsync<AiMetricsBreakdownRowSql>()).ToList();
		var byWorker = (await grid.ReadAsync<AiMetricsBreakdownRowSql>()).ToList();
		var byProvider = (await grid.ReadAsync<AiMetricsBreakdownRowSql>()).ToList();

		return new AiRequestLogMetrics(
			totalsRow.ToDomain(),
			timeSeries.Select(t => t.ToDomain()).ToList(),
			byModel.Select(r => r.ToDomain()).ToList(),
			byWorker.Select(r => r.ToDomain()).ToList(),
			byProvider.Select(r => r.ToDomain()).ToList());
	}

	private static (string Sql, DynamicParameters Params) BuildWhere(AiRequestLogFilter filter)
	{
		var clauses = new List<string>();
		var parameters = new DynamicParameters();

		if (filter.From is not null)
		{
			clauses.Add(@"""Timestamp"" >= @from");
			parameters.Add("from", filter.From.Value.ToUniversalTime());
		}

		if (filter.To is not null)
		{
			var to = filter.To.Value;
			if (to.TimeOfDay == TimeSpan.Zero)
				to = to.AddDays(1);
			clauses.Add(@"""Timestamp"" < @to");
			parameters.Add("to", to.ToUniversalTime());
		}

		if (!string.IsNullOrWhiteSpace(filter.Provider))
		{
			clauses.Add(@"""Provider"" = @provider");
			parameters.Add("provider", filter.Provider);
		}

		if (!string.IsNullOrWhiteSpace(filter.Worker))
		{
			clauses.Add(@"""Worker"" = @worker");
			parameters.Add("worker", filter.Worker);
		}

		if (!string.IsNullOrWhiteSpace(filter.Model))
		{
			clauses.Add(@"""Model"" = @model");
			parameters.Add("model", filter.Model);
		}

		var whereClause = clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);
		return (whereClause, parameters);
	}

	private static void AppendStatusClause(AiRequestLogFilter filter, ref string whereClause, DynamicParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(filter.Status))
			return;

		whereClause += @" AND ""Status"" = @status";
		parameters.Add("status", filter.Status);
	}

	private static void AppendSearchClause(AiRequestLogFilter filter, ref string whereClause, DynamicParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(filter.Search))
			return;

		var pattern = $"%{QueryHelpers.EscapeILikePattern(filter.Search)}%";
		whereClause += @" AND (""Operation"" ILIKE @pattern ESCAPE '\' OR ""Model"" ILIKE @pattern ESCAPE '\' OR ""ErrorMessage"" ILIKE @pattern ESCAPE '\')";
		parameters.Add("pattern", pattern);
	}

	private sealed record AiMetricsTotalsRow(
		decimal TotalCostUsd,
		long TotalCalls,
		long SuccessCalls,
		long ErrorCalls,
		decimal AverageLatencyMs,
		long TotalInputTokens,
		long TotalOutputTokens,
		long TotalCacheCreationInputTokens,
		long TotalCacheReadInputTokens)
	{
		public AiMetricsTotals ToDomain() => new(
			TotalCostUsd, (int)TotalCalls, (int)SuccessCalls, (int)ErrorCalls, (double)AverageLatencyMs,
			(int)TotalInputTokens, (int)TotalOutputTokens, (int)TotalCacheCreationInputTokens, (int)TotalCacheReadInputTokens);
	}

	private sealed record AiMetricsTimeBucketRow(
		DateTime Bucket,
		string Provider,
		decimal CostUsd,
		long Calls,
		long Tokens)
	{
		public AiMetricsTimeBucket ToDomain() => new(
			new DateTimeOffset(DateTime.SpecifyKind(Bucket, DateTimeKind.Utc)),
			Provider, CostUsd, (int)Calls, (int)Tokens);
	}

	private sealed record AiMetricsBreakdownRowSql(
		string Key,
		long Calls,
		decimal CostUsd,
		long Tokens)
	{
		public AiMetricsBreakdownRow ToDomain() => new(Key, (int)Calls, CostUsd, (int)Tokens);
	}
}
