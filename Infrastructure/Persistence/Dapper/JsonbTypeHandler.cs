using System.Data;
using System.Text.Json;
using Dapper;
using NpgsqlTypes;

namespace Infrastructure.Persistence.Dapper;

internal sealed class JsonbTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    public override T? Parse(object value) =>
        JsonSerializer.Deserialize<T>((string)value);

    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        parameter.Value = value is null ? DBNull.Value : (object)JsonSerializer.Serialize(value);
        if (parameter is Npgsql.NpgsqlParameter npgsqlParam)
            npgsqlParam.NpgsqlDbType = NpgsqlDbType.Jsonb;
    }
}
