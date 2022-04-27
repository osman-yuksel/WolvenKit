using WolvenKit.RED4.Types;
using WolvenKit.Core.Extensions;
using WolvenKit.RED4.Save.IO;

namespace WolvenKit.RED4.Save
{
    public class ItemDropStorageManager : INodeData
    {
        public List<ItemDropStorage> ItemDropStorages { get; set; } = new();
        public byte[] TrailingBytes { get; set; }
    }

    public class ItemDropStorageManagerParser : INodeParser
    {
        public static string NodeName => Constants.NodeNames.ITEM_DROP_STORAGE_MANAGER;

        public void Read(BinaryReader reader, NodeEntry node)
        {
            var startPos = reader.BaseStream.Position;
            //var bytes = reader.ReadBytes(node.Size);
            //reader.BaseStream.Position = startPos;
            //
            //File.WriteAllBytes(@"C:\Dev\C77\asd.bin", bytes);

            var data = new ItemDropStorageManager();

            var parser = new ItemDropStorageParser();


            var cnt = reader.ReadInt32();
            for (int i = 0; i < cnt; i++)
            {
                node.Children[i].ReadByParent = true;
                parser.Read(reader, node.Children[i]);
                data.ItemDropStorages.Add((ItemDropStorage)node.Children[i].Value);
            }

            var remaining = node.Size - (reader.BaseStream.Position - startPos);
            data.TrailingBytes = reader.ReadBytes((int)remaining);

            node.Value = data;
        }

        public void Write(NodeWriter writer, NodeEntry node) => throw new NotImplementedException();
    }
}
