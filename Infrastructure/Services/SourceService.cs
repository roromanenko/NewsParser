using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class SourceService(
	ISourceRepository sourceRepository,
	ILogger<SourceService> logger) : ISourceService
{
	public async Task<List<Source>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		return await sourceRepository.GetAllAsync(cancellationToken);
	}

	public async Task<Source> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		return await sourceRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"Source {id} not found");
	}

	public async Task<Source> CreateAsync(
		string name,
		string url,
		SourceType type,
		CancellationToken cancellationToken = default)
	{
		var exists = await sourceRepository.ExistsByUrlAsync(url, cancellationToken);
		if (exists)
			throw new InvalidOperationException($"Source with URL {url} already exists");

		var source = new Source
		{
			Id = Guid.NewGuid(),
			Name = name,
			Url = url,
			Type = type,
			IsActive = true,
		};

		var created = await sourceRepository.CreateAsync(source, cancellationToken);
		logger.LogInformation("Source {SourceId} created: {SourceName}", created.Id, created.Name);
		return created;
	}

	public async Task<Source> UpdateAsync(
		Guid id,
		string name,
		string url,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		var source = await sourceRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"Source {id} not found");

		if (source.Url != url)
		{
			var exists = await sourceRepository.ExistsByUrlAsync(url, cancellationToken);
			if (exists)
				throw new InvalidOperationException($"Source with URL {url} already exists");
		}

		source.Name = name;
		source.Url = url;
		source.IsActive = isActive;

		await sourceRepository.UpdateAsync(source, cancellationToken);
		logger.LogInformation("Source {SourceId} updated", source.Id);
		return source;
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var exists = await sourceRepository.GetByIdAsync(id, cancellationToken);
		if (exists is null)
			throw new KeyNotFoundException($"Source {id} not found");

		await sourceRepository.DeleteAsync(id, cancellationToken);
		logger.LogInformation("Source {SourceId} deleted", id);
	}
}
