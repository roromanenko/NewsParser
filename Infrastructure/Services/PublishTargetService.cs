using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class PublishTargetService(
	IPublishTargetRepository publishTargetRepository,
	ILogger<PublishTargetService> logger) : IPublishTargetService
{
	public async Task<List<PublishTarget>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		return await publishTargetRepository.GetAllAsync(cancellationToken);
	}

	public async Task<List<PublishTarget>> GetAllByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
	{
		return await publishTargetRepository.GetAllByProjectAsync(projectId, cancellationToken);
	}

	public async Task<List<PublishTarget>> GetActiveAsync(CancellationToken cancellationToken = default)
	{
		return await publishTargetRepository.GetActiveAsync(cancellationToken);
	}

	public Task<List<PublishTarget>> GetActiveByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
		=> publishTargetRepository.GetActiveByProjectAsync(projectId, cancellationToken);

	public async Task<PublishTarget> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		return await publishTargetRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"PublishTarget {id} not found");
	}

	public async Task<PublishTarget> CreateAsync(
		string name,
		Platform platform,
		string identifier,
		string systemPrompt,
		Guid projectId,
		CancellationToken cancellationToken = default)
	{
		var target = new PublishTarget
		{
			Id = Guid.NewGuid(),
			Name = name,
			Platform = platform,
			Identifier = identifier,
			SystemPrompt = systemPrompt,
			IsActive = true,
			ProjectId = projectId,
		};

		var created = await publishTargetRepository.CreateAsync(target, cancellationToken);
		logger.LogInformation("PublishTarget {PublishTargetId} created: {Name}", created.Id, created.Name);
		return created;
	}

	public async Task<PublishTarget> UpdateAsync(
		Guid id,
		string name,
		string identifier,
		string systemPrompt,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		var target = await publishTargetRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"PublishTarget {id} not found");

		target.Name = name;
		target.Identifier = identifier;
		target.SystemPrompt = systemPrompt;
		target.IsActive = isActive;

		await publishTargetRepository.UpdateAsync(target, cancellationToken);
		logger.LogInformation("PublishTarget {PublishTargetId} updated", target.Id);
		return target;
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var target = await publishTargetRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"PublishTarget {id} not found");

		await publishTargetRepository.DeleteAsync(target.Id, cancellationToken);
		logger.LogInformation("PublishTarget {PublishTargetId} deleted", id);
	}
}
