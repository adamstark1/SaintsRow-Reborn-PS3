namespace SaintsRowReborn.Hydra.DataTypes
{
    public interface IHydraItem
    {
        HydraDataType DataType { get; }

        void Serialize(Stream s);
        void Deserialize(Stream s);

        string GetString();
        string GetString(int indentLevel);
    }
}
