using Dapper;
using Pgvector;

namespace Infrastructure.Persistence.Dapper;

internal static class DapperTypeHandlers
{
    public static void Register()
    {
        SqlMapper.AddTypeHandler(new VectorTypeHandler());
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<List<string>>());
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<List<Guid>>());
    }
}
