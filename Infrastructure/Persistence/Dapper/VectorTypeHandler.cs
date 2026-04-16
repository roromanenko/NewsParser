using System.Data;
using Dapper;
using NpgsqlTypes;
using Pgvector;

namespace Infrastructure.Persistence.Dapper;

internal sealed class VectorTypeHandler : SqlMapper.TypeHandler<Vector>
{
#pragma warning disable CS8765
    public override Vector Parse(object value) => new((float[])value);
#pragma warning restore CS8765

    public override void SetValue(IDbDataParameter parameter, Vector value)
    {
        parameter.Value = value;
        if (parameter is Npgsql.NpgsqlParameter npgsqlParam)
            npgsqlParam.NpgsqlDbType = NpgsqlDbType.Unknown;
    }
}
