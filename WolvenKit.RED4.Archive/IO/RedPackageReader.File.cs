using System.Collections.Generic;
using System.Linq;
using System.Text;
using WolvenKit.Core.Extensions;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Types;
using WolvenKit.RED4.Types.Exceptions;

namespace WolvenKit.RED4.Archive.IO
{
    public partial class RedPackageReader : IBufferReader
    {
        private RedPackageHeader header;

        public virtual EFileReadErrorCodes ReadBuffer(RedBuffer buffer)
        {
            var chunks = new List<RedBaseClass>();

            var result = new RedPackage();
            _outputFile = result;

            header.version = BaseReader.ReadUInt16();
            if (header.version < 2 || header.version > 4)
            {
                return EFileReadErrorCodes.UnsupportedVersion;
            }

            header.numSections = _reader.ReadUInt16();
            if (header.numSections < 6 || header.numSections > 7)
            {
                return EFileReadErrorCodes.UnsupportedVersion;
            }

            result.Version = header.version;
            result.Sections = header.numSections;

            header.numComponents = _reader.ReadUInt32();

            if (header.numSections == 7)
            {
                header.refPoolDescOffset = _reader.ReadUInt32();
                header.refPoolDataOffset = _reader.ReadUInt32();
            }

            header.namePoolDescOffset = _reader.ReadUInt32();
            header.namePoolDataOffset = _reader.ReadUInt32();

            header.chunkDescOffset = _reader.ReadUInt32();
            header.chunkDataOffset = _reader.ReadUInt32();

            if (header.numSections == 7 && header.refPoolDescOffset != 0)
            {
                return EFileReadErrorCodes.NoCr2w;
            }

            if (header.refPoolDescOffset != 0)
            {
                return EFileReadErrorCodes.NoCr2w;
            }

            if (Settings.RedPackageType == RedPackageType.Default)
            {
                result.CruidIndex = _reader.ReadInt16();

                var numCruids = _reader.ReadUInt16();
                for (var i = 0; i < numCruids; i++)
                {
                    result.RootCruids.Add(_reader.ReadUInt64());
                }
            }

            if (Settings.RedPackageType == RedPackageType.SaveResource || Settings.RedPackageType == RedPackageType.ScriptableSystem)
            {
                var numCruids = _reader.ReadUInt32();
                for (var i = 0; i < numCruids; i++)
                {
                    result.RootCruids.Add(_reader.ReadUInt64());
                }
            }

            var baseOff = BaseStream.Position;

            // read refs
            var refCount = (header.refPoolDataOffset - header.refPoolDescOffset) / 4;
            BaseStream.Position = baseOff + header.refPoolDescOffset;
            var refDesc = BaseStream.ReadStructs<RedPackageImportHeader>(refCount);

            foreach (var r in refDesc)
            {
                BaseStream.Position = baseOff + r.offset;
                importsList.Add(ReadImport(r));
            }

            // read strings
            var nameCount = (header.namePoolDataOffset - header.namePoolDescOffset) / 4;
            BaseStream.Position = baseOff + header.namePoolDescOffset;
            var nameDesc = BaseStream.ReadStructs<RedPackageNameHeader>(nameCount);

            foreach (var s in nameDesc)
            {
                BaseStream.Position = baseOff + s.offset;
                _namesList.Add(ReadName(s));
            }

            // read chunks
            var chunkCount = (header.chunkDataOffset - header.chunkDescOffset) / 8;
            BaseStream.Position = baseOff + header.chunkDescOffset;
            var chunkDesc = BaseStream.ReadStructs<RedPackageChunkHeader>(chunkCount);

            for (var i = 0; i < chunkDesc.Length; i++)
            {
                chunks.Add(ReadChunk(chunkDesc[i]));
            }

            var newChunks = new List<RedBaseClass>();
            for (var i = 0; i < chunks.Count; i++)
            {
                if (!HandleQueue.ContainsKey(i))
                {
                    newChunks.Add(chunks[i]);
                    continue;
                }

                foreach (var handle in HandleQueue[i])
                {
                    handle.SetValue(chunks[i]);
                }
            }

            result.Chunks = newChunks;

            if (Settings.RedPackageType is RedPackageType.Default or RedPackageType.SaveResource)
            {
                for (int i = 0; i < result.Chunks.Count; i++)
                {
                    result.ChunkDictionary.Add(result.Chunks[i], result.RootCruids[i]);
                }
            }

            buffer.Data = result;

            return EFileReadErrorCodes.NoError;
        }

        private PackageImport ReadImport(RedPackageImportHeader r)
        {
            // needs header offset
            //Debug.Assert(BaseStream.Position == r.offset);

            var import = new PackageImport()
            {
                Flags = (InternalEnums.EImportFlags)(r.sync ? 0b1 : 0b0)
            };
            if (Settings.ImportsAsHash)
            {
                if (r.size != 8)
                {
                    throw new TodoException("ReadAsHash size != 8");
                }

                import.DepotPath = _reader.ReadUInt64();

                if (CollectData && import.DepotPath.IsResolvable)
                {
                    if (import.DepotPath.IsResolvable)
                    {
                        DataCollection.RawImportList.Add(import.DepotPath);
                    }
                    else
                    {
                        DataCollection.RawImportHashList.Add(import.DepotPath);
                    }
                }
            }
            else
            {
                var bytes = _reader.ReadBytes(r.size);
                import.DepotPath = Encoding.UTF8.GetString(bytes.ToArray());

                if (CollectData)
                {
                    DataCollection.RawImportList.Add(import.DepotPath);
                }
            }
            return import;
        }
        private string ReadName(RedPackageNameHeader n)
        {
            var s = _reader.ReadNullTerminatedString();
            //Debug.Assert(s.Length == n.size);
            if (CollectData)
            {
                DataCollection.RawStringList.Add(s);
            }

            return s;
        }

        protected virtual RedBaseClass ReadChunk(RedPackageChunkHeader c)
        {
            // needs header offset
            //Debug.Assert(BaseStream.Position == c.offset);
            var redTypeName = GetStringValue((ushort)c.typeID);
            var (type, _) = RedReflection.GetCSTypeFromRedType(redTypeName);

            var instance = RedTypeManager.Create(type);
            if (instance is DynamicBaseClass dbc)
            {
                dbc.ClassName = redTypeName;
            }

            ReadClass(instance, 0);

            return instance;
        }
    }

    public class PackageImport : IRedImport
    {
        public CName DepotPath { get; set; }
        public InternalEnums.EImportFlags Flags { get; set; }
    }
}
