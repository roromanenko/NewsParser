using System.Data;
using Dapper;
using Pgvector;

namespace Infrastructure.Persistence.Dapper;

internal sealed class VectorTypeHandler : SqlMapper.TypeHandler<Vector>
{
    public override Vector Parse(object value) => value switch
    {
        Vector v => v,
        float[] f => new Vector(f),
        _ => throw new InvalidCastException($"Cannot convert {value?.GetType().FullName ?? "null"} to Vector."),
    };

    public override void SetValue(IDbDataParameter parameter, Vector value)
    {
        parameter.Value = value;
        if (parameter is Npgsql.NpgsqlParameter npgsqlParam)
            npgsqlParam.DataTypeName = "vector";
    }
}
