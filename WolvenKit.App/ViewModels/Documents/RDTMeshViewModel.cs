using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CP77.CR2W;
using ReactiveUI.Fody.Helpers;
using Splat;
using WolvenKit.Common.Services;
using WolvenKit.Functionality.Ab4d;
using WolvenKit.Functionality.Services;
using WolvenKit.Modkit.RED4;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.Types;

namespace WolvenKit.ViewModels.Documents
{

    public interface IBindable
    {
        public Matrix3D Matrix { get; set; }
        public string BindName { get; set; }
        public string SlotName { get; set; }
    }

    public class Appearance
    {
        public string AppearanceName { get; set; }
        public string Name { get; set; }
        public List<LoadableModel> Models { get; set; }
        public CName Resource { get; set; }
    }

    public class LoadableModel : IBindable
    {
        public string FilePath { get; set; }
        public Model3D Model { get; set; }
        public Transform3D Transform { get; set; }
        public bool IsEnabled { get; set; }
        public string Name { get; set; }
        public List<Material> Materials { get; set; } = new();

        public Matrix3D Matrix { get; set; }
        public string BindName { get; set; }
        public string SlotName { get; set; }
    }

    public class Rig : IBindable
    {
        public string Name { get; set; }
        public List<RigBone> Bones { get; set; }
        public Rig Parent { get; set; }
        public List<Rig> Children { get; set; } = new();

        public Matrix3D Matrix { get; set; }
        public string BindName { get; set; }
        public string SlotName { get; set; }

        public void AddChild(Rig child)
        {
            child.Parent = this;
            Children.Add(child);
        }
    }

    public class RigBone
    {
        public string Name { get; set; }
        public RigBone Parent { get; set; }
        public List<RigBone> Children { get; set; } = new();
        public Matrix3D Matrix { get; set; }

        public void AddChild(RigBone child)
        {
            child.Parent = this;
            Children.Add(child);
        }
    }

    public class SlotSet : IBindable
    {
        public string Name { get; set; }
        public Dictionary<string, string> Slots { get; set; }

        public Matrix3D Matrix { get; set; }
        public string BindName { get; set; }
        public string SlotName { get; set; }
    }

    public class Material
    {
        public string Name { get; set; }
        public CMaterialInstance Instance { get; set; }
        public Dictionary<string, object> Values { get; set; } = new();
        public Material Base { get; set; }
        public Bitmap ColorTexture { get; set; }
        public string ColorTexturePath { get; set; }
    }

    public class RDTMeshViewModel : RedDocumentTabViewModel
    {
        protected readonly RedBaseClass _data;
        public RedDocumentViewModel File;
        private Dictionary<string, LoadableModel> _modelList = new();
        private Dictionary<string, SlotSet> _slotSets = new();

        public RDTMeshViewModel(RedDocumentViewModel file)
        {
            Header = "Preview";
            File = file;
        }

        public RDTMeshViewModel(CMesh data, RedDocumentViewModel file) : this(file)
        {
            _data = data;

            var materials = new Dictionary<string, Material>();

            var localList = (CR2WList)data.LocalMaterialBuffer.RawData?.Buffer.Data ?? null;

            foreach (var me in data.MaterialEntries)
            {
                if (!me.IsLocalInstance)
                {
                    materials.Add(me.Name, new Material()
                    {
                        Name = me.Name
                    });
                    continue;
                }
                CMaterialInstance inst = null;

                if (localList != null)
                {
                    inst = (CMaterialInstance)localList.Files[me.Index].RootChunk;
                }
                else
                {
                    //foreach (var pme in data.PreloadLocalMaterialInstances)
                    //{
                        //inst = (CMaterialInstance)pme.GetValue();
                    //}
                   inst = (CMaterialInstance)data.PreloadLocalMaterialInstances[me.Index].GetValue();
                }

                //CMaterialInstance bm = null;
                //if (File.GetFileFromDepotPath(inst.BaseMaterial.DepotPath) is var file)
                //{
                //    bm = (CMaterialInstance)file.RootChunk;
                //}
                var material = new Material()
                {
                    Instance = inst,
                    Name = me.Name
                };

                foreach (var pair in inst.Values)
                {
                    material.Values.Add(pair.Key, pair.Value);
                }

                materials.Add(me.Name, material);
            }

            var outPath = Path.Combine(ISettingsManager.GetTemp_OBJPath(), Path.GetFileNameWithoutExtension(file.FilePath) + "_full.glb");
            if (System.IO.File.Exists(outPath) || MeshTools.ExportMesh(file.Cr2wFile, new FileInfo(outPath)))
            {
                foreach (var handle in data.Appearances)
                {
                    var app = handle.GetValue();
                    if (app is meshMeshAppearance mmapp)
                    {
                        var appMaterials = new List<Material>();

                        foreach (var m in mmapp.ChunkMaterials)
                        {
                            if (materials.ContainsKey(m))
                            {
                                appMaterials.Add(materials[m]);
                            }
                            else
                            {
                                appMaterials.Add(new Material()
                                {
                                    Name = m
                                });
                            }
                        }

                        var list = new List<LoadableModel>();

                        list.Add(new LoadableModel()
                        {
                            FilePath = outPath,
                            IsEnabled = true,
                            Name = file.RelativePath,
                            Materials = appMaterials
                        });

                        var a = new Appearance()
                        {
                            Name = mmapp.Name,
                            Models = list
                        };
                        Appearances.Add(a);
                    }
                }
                SelectedAppearance = Appearances[0];
            }
        }

        public RDTMeshViewModel(entEntityTemplate ent, RedDocumentViewModel file) : this(file)
        {
            _data = ent;

            if (ent.CompiledData.Data is not Package04 pkg)
                return;

            if (ent.Appearances.Count > 0)
            {

                foreach (var component in pkg.Chunks)
                {
                    if (component is entSlotComponent slotset)
                    {
                        var slots = new Dictionary<string, string>();
                        foreach (var slot in slotset.Slots)
                        {
                            if (!slots.ContainsKey(slot.SlotName))
                                slots.Add(slot.SlotName, slot.BoneName);
                        }

                        string bindName = null, slotName = null;
                        if ((slotset.ParentTransform?.GetValue() ?? null) is entHardTransformBinding ehtb)
                        {
                            bindName = ehtb.BindName;
                            slotName = ehtb.SlotName;
                        }

                        _slotSets.Add(slotset.Name, new SlotSet()
                        {
                            Name = slotset.Name,
                            Matrix = ToMatrix3D(slotset.LocalTransform),
                            Slots = slots,
                            BindName = bindName,
                            SlotName = slotName
                        });
                    }

                    if (component is entAnimatedComponent enc)
                    {
                        var rigFile = File.GetFileFromDepotPath(enc.Rig.DepotPath);

                        if (rigFile.RootChunk is animRig rig)
                        {
                            var rigBones = new List<RigBone>();
                            for (int i = 0; i < rig.BoneNames.Count; i++)
                            {
                                var rigBone = new RigBone()
                                {
                                    Name = rig.BoneNames[i],
                                    Matrix = ToMatrix3D(rig.BoneTransforms[i])
                                };

                                if (rig.BoneParentIndexes[i] != -1)
                                {
                                    rigBones[rig.BoneParentIndexes[i]].AddChild(rigBone);
                                }

                                rigBones.Add(rigBone);
                            }

                            string bindName = null, slotName = null;
                            if ((enc.ParentTransform?.GetValue() ?? null) is entHardTransformBinding ehtb)
                            {
                                bindName = ehtb.BindName;
                                slotName = ehtb.SlotName;
                            }

                            Rigs.Add(enc.Name, new Rig()
                            {
                                Name = enc.Name,
                                Bones = rigBones,
                                BindName = bindName,
                                SlotName = slotName
                            });
                        }
                    }
                }

                foreach (var rig in Rigs.Values)
                {
                    if (rig.BindName != null && Rigs.ContainsKey(rig.BindName))
                    {
                        Rigs[rig.BindName].AddChild(rig);
                    }
                }

                foreach (var app in ent.Appearances)
                {
                    var appFile = File.GetFileFromDepotPath(app.AppearanceResource.DepotPath);

                    if (appFile == null || appFile.RootChunk is not appearanceAppearanceResource aar)
                    {
                        continue;
                    }

                    foreach (var handle in aar.Appearances)
                    {
                        var appDef = (appearanceAppearanceDefinition)handle.GetValue();

                        if (appDef.Name.ToString() != app.AppearanceName.ToString() || appDef.CompiledData.Data is not Package04 appPkg)
                        {
                            continue;
                        }

                        Appearances.Add(new Appearance()
                        {
                            AppearanceName = app.AppearanceName,
                            Name = app.Name,
                            Resource = app.AppearanceResource.DepotPath,
                            Models = LoadMeshs(appPkg.Chunks)
                        });

                        break;
                    }
                }
                //var j = 0;
                //foreach (var a in Appearances)
                //{
                //    var appFile = File.GetFileFromDepotPath(a.Resource);

                //    if (appFile != null && appFile.RootChunk is appearanceAppearanceResource app && app.Appearances.Count > (j + 1) && app.Appearances[j].GetValue() is appearanceAppearanceDefinition appDef && appDef.CompiledData.Data is Package04 appPkg)
                //    {
                //    }
                //    j++;
                //}

                if (Appearances.Count > 0)
                    SelectedAppearance = Appearances[0];
            }
            else
            {
                Appearances.Add(new Appearance()
                {
                    Name = "Default",
                    Models = LoadMeshs(pkg.Chunks)
                });

                SelectedAppearance = Appearances[0];
            }
        }

        public RDTMeshViewModel(appearanceAppearanceResource app, RedDocumentViewModel file) : this(file)
        {
            _data = app;
            foreach (var a in app.Appearances)
            {
                if (a.GetValue() is appearanceAppearanceDefinition appDef && appDef.CompiledData.Data is Package04 pkg)
                {
                    Appearances.Add(new Appearance()
                    {
                        Name = appDef.Name,
                        Models = LoadMeshs(pkg.Chunks)
                    });

                }
            }

            SelectedAppearance = Appearances[0];
        }

        private List<LoadableModel> LoadMeshs(IList<RedBaseClass> chunks)
        {
            if (chunks == null)
                return null;

            var appModels = new Dictionary<string, LoadableModel>();

            foreach (var component in chunks)
            {
                Vector3 scale = new Vector3() { X = 1, Y = 1, Z = 1 };
                CName depotPath = null;
                bool enabled = true;
                string meshApp = "";

                if (component is entMeshComponent emc)
                {
                    depotPath = emc.Mesh.DepotPath;
                    scale = emc.VisualScale;
                    enabled = emc.IsEnabled;
                    meshApp = emc.MeshAppearance;
                }
                else if (component is entSkinnedMeshComponent esmc)
                {
                    depotPath = esmc.Mesh.DepotPath;
                    meshApp = esmc.MeshAppearance;
                }

                if (component is entIPlacedComponent epc && depotPath != null)
                {

                    var meshFile = File.GetFileFromDepotPath(depotPath);

                    if (meshFile == null || meshFile.RootChunk is not CMesh mesh)
                    {
                        Locator.Current.GetService<ILoggerService>().Warning($"Couldn't find mesh file: {depotPath} / {depotPath.GetRedHash()}");
                        continue;
                    }

                    var matrix = ToMatrix3D(epc.LocalTransform);

                    string bindName = null, slotName = null;
                    if ((epc.ParentTransform?.GetValue() ?? null) is entHardTransformBinding ehtb)
                    {
                        bindName = ehtb.BindName;
                        slotName = ehtb.SlotName;
                    }

                    matrix.Scale(ToScaleVector3D(scale));

                    var materials = new Dictionary<string, Material>();

                    var localList = (CR2WList)mesh.LocalMaterialBuffer.RawData?.Buffer.Data ?? null;

                    if (localList != null)
                    {
                        foreach (var me in mesh.MaterialEntries)
                        {
                            if (!me.IsLocalInstance)
                            {
                                materials.Add(me.Name, new Material()
                                {
                                    Name = me.Name
                                });
                                continue;
                            }

                            var inst = (CMaterialInstance)localList.Files[me.Index].RootChunk;

                            //CMaterialInstance bm = null;
                            //if (File.GetFileFromDepotPath(inst.BaseMaterial.DepotPath) is var file)
                            //{
                            //    bm = (CMaterialInstance)file.RootChunk;
                            //}

                            var material = new Material()
                            {
                                Instance = inst,
                                Name = me.Name
                            };

                            foreach (var pair in inst.Values)
                            {
                                material.Values.Add(pair.Key, pair.Value);
                            }

                            materials.Add(me.Name, material);
                        }
                    }

                    var outPath = Path.Combine(ISettingsManager.GetTemp_OBJPath(), Path.GetFileNameWithoutExtension(depotPath) + "_" + depotPath.GetRedHash().ToString() + "_full.glb");
                    //var outPath = Path.Combine(ISettingsManager.GetTemp_OBJPath(), Path.GetFileName(depotPath) + "_" + depotPath.GetRedHash().ToString()) + "_full.glb";
                    if (System.IO.File.Exists(outPath) || MeshTools.ExportMesh(meshFile, new FileInfo(outPath)))
                    {
                        foreach (var handle in mesh.Appearances)
                        {
                            var app = handle.GetValue();
                            if (app is meshMeshAppearance mmapp && mmapp.Name == meshApp)
                            {
                                var appMaterials = new List<Material>();

                                foreach (var m in mmapp.ChunkMaterials)
                                {
                                    if (materials.ContainsKey(m))
                                    {
                                        appMaterials.Add(materials[m]);
                                    }
                                    else
                                    {
                                        appMaterials.Add(new Material()
                                        {
                                            Name = m
                                        });
                                    }
                                }

                                appModels.Add(epc.Name, new LoadableModel()
                                {
                                    FilePath = outPath,
                                    Matrix = matrix,
                                    IsEnabled = enabled,
                                    Name = epc.Name,
                                    BindName = bindName,
                                    SlotName = slotName,
                                    Materials = appMaterials
                                });
                                break;
                            }
                        }

                        //if (!appModels.ContainsKey(epc.Name))
                        //{
                        //    appModels.Add(epc.Name, new LoadableModel()
                        //    {
                        //        FilePath = outPath,
                        //        Matrix = matrix,
                        //        IsEnabled = enabled,
                        //        Name = epc.Name,
                        //        BindName = bindName,
                        //        SlotName = slotName,
                        //        Materials = materials
                        //    });
                        //}
                    }



                }
            }

            var list = new List<LoadableModel>();

            foreach (var model in appModels.Values)
            {
                var matrix = new Matrix3D();
                GetResolvedMatrix(model, ref matrix, appModels);
                model.Transform = new MatrixTransform3D(matrix);
                if (model.Name.Contains("shadow") || model.Name.Contains("AppearanceProxyMesh") || model.Name.Contains("sticker") || model.Name.Contains("cutout"))
                {
                    model.IsEnabled = false;
                }
                list.Add(model);
            }

            if (list.Count != 0)
            {
                list.Sort((a, b) => a.Name.CompareTo(b.Name));
                return list;
            }

            return null;
        }

        public async Task LoadMaterial(Material material)
        {
            //if (material.ColorTexturePath != null)
            //    return;

            if (System.IO.File.Exists(Path.Combine(ISettingsManager.GetTemp_OBJPath(), material.Name + ".png")))
                return;

            var dictionary = material.Values;

            var mat = material.Instance;
            while (mat != null && mat.BaseMaterial != null)
            {
                var baseMaterialFile = File.GetFileFromDepotPath(mat.BaseMaterial.DepotPath);
                if (baseMaterialFile.RootChunk is CMaterialInstance cmi)
                {
                    foreach (var pair in cmi.Values)
                    {
                        if (!dictionary.ContainsKey(pair.Key))
                            dictionary.Add(pair.Key, pair.Value);
                    }
                    mat = cmi;
                }
                else
                {
                    mat = null;
                }
            }

            if (dictionary.ContainsKey("MultilayerSetup") && dictionary.ContainsKey("MultilayerMask"))
            {
                if (dictionary["MultilayerSetup"] is not CResourceReference<Multilayer_Setup> mlsRef)
                {
                    return;
                }

                if (dictionary["MultilayerMask"] is not CResourceReference<Multilayer_Mask> mlmRef)
                {
                    return;
                }

                var setupFile = File.GetFileFromDepotPath(mlsRef.DepotPath);

                if (setupFile.RootChunk is not Multilayer_Setup mls)
                {
                    return;
                }

                var maskFile = File.GetFileFromDepotPath(mlmRef.DepotPath);

                if (maskFile.RootChunk is not Multilayer_Mask mlm)
                {
                    return;
                }

                ModTools.ConvertMultilayerMaskToDdsStreams(mlm, out var streams);

                Bitmap destBitmap = new Bitmap(1024, 1024);

                using (Graphics gfx = Graphics.FromImage(destBitmap))
                {
                    var i = 0;
                    foreach (var layer in mls.Layers)
                    {
                        if (layer.ColorScale == "null_null" || layer.Opacity == 0 || layer.Material == null)
                        {
                            goto SkipLayer;
                        }

                        var templateFile = File.GetFileFromDepotPath(layer.Material.DepotPath);

                        if (templateFile.RootChunk is not Multilayer_LayerTemplate mllt)
                        {
                            goto SkipLayer;
                        }

                        foreach (var color in mllt.Overrides.ColorScale)
                        {
                            if (color.N.ToString() == layer.ColorScale.ToString())
                            {
                                var bitmap = await ImageDecoder.RenderToBitmapSourceDds(streams[i]);
                                bitmap = new TransformedBitmap(bitmap, new ScaleTransform(1, -1));

                                Bitmap sourceBitmap;
                                using (var outStream = new MemoryStream())
                                {
                                    BitmapEncoder enc = new PngBitmapEncoder();
                                    enc.Frames.Add(BitmapFrame.Create(bitmap));
                                    enc.Save(outStream);
                                    sourceBitmap = new Bitmap(outStream);
                                }

                                var colorMatrix = new ColorMatrix(new float[][]
                                {
                                    new float[] { 0, 0, 0, 0, 0},
                                    new float[] { 0, 0, 0, 0, 0},
                                    new float[] { 0, 0, 0, 0, 0},
                                    new float[] { 0, 0, 0, 0, 0},
                                    new float[] { 0, 0, 0, 0, 0},
                                });
                                colorMatrix.Matrix03 = layer.Opacity;
                                colorMatrix.Matrix40 = color.V[0];
                                colorMatrix.Matrix41 = color.V[1];
                                colorMatrix.Matrix42 = color.V[2];

                                ImageAttributes attributes = new ImageAttributes();

                                attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                                gfx.DrawImage(sourceBitmap, new Rectangle(0, 0, 1024, 1024), 0, 0, sourceBitmap.Width, sourceBitmap.Height, GraphicsUnit.Pixel, attributes);
                            }
                        }

                    SkipLayer:
                        i++;
                    }
                }

                try
                {
                    destBitmap.Save(Path.Combine(ISettingsManager.GetTemp_OBJPath(), material.Name + ".png"), ImageFormat.Png);
                    destBitmap.Dispose();
                }
                catch (Exception e)
                {
                    Locator.Current.GetService<ILoggerService>().Error(e.Message);
                }
            }
            else if (dictionary.ContainsKey("DiffuseTexture") && dictionary["DiffuseTexture"] is CResourceReference<ITexture> crr)
            {
                var xbm = File.GetFileFromDepotPath(crr.DepotPath);

                if (xbm.RootChunk is not ITexture it)
                {
                    return;
                }

                //var opacity = dictionary.ContainsKey("DiffuseAlpha") ? (float)(CFloat)dictionary["DiffuseAlpha"] : 1F;

                //if (opacity == 0)
                //{
                //    return;
                //}

                //var color = dictionary.ContainsKey("DiffuseColor") ? (CColor)dictionary["DiffuseColor"] : new CColor() { Red = 255, Green = 255, Blue = 255, Alpha = 255};

                var stream = new MemoryStream();
                ModTools.ConvertRedClassToDdsStream(it, stream, out var format);


                var bitmap = await ImageDecoder.RenderToBitmapSourceDds(stream);

                Bitmap sourceBitmap;
                using (var outStream = new MemoryStream())
                {
                    BitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bitmap));
                    enc.Save(outStream);
                    sourceBitmap = new Bitmap(outStream);
                }
                //bitmap = new TransformedBitmap(bitmap, new ScaleTransform(1, -1));

                Bitmap destBitmap = new Bitmap((int)sourceBitmap.Width, (int)sourceBitmap.Height);
                using (Graphics gfx = Graphics.FromImage(destBitmap))
                {
                    //var colorMatrix = new ColorMatrix(new float[][]
                    //{
                    //    new float[] { 0, 0, 0, 0, 0},
                    //    new float[] { 0, 0, 0, 0, 0},
                    //    new float[] { 0, 0, 0, 0, 0},
                    //    new float[] { 0, 0, 0, 0, 0},
                    //    new float[] { 0, 0, 0, 0, 0},
                    //});
                    //colorMatrix.Matrix03 = opacity;
                    //colorMatrix.Matrix40 = color.Red / 256F;
                    //colorMatrix.Matrix41 = color.Green / 256F;
                    //colorMatrix.Matrix42 = color.Blue / 256F;

                    ImageAttributes attributes = new ImageAttributes();

                    //attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                    gfx.DrawImage(sourceBitmap, new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height), 0, 0, sourceBitmap.Width, sourceBitmap.Height, GraphicsUnit.Pixel, attributes);
                }

                try
                {
                    //sourceBitmap.MakeTransparent(System.Drawing.Color.Black);
                    sourceBitmap.Save(Path.Combine(ISettingsManager.GetTemp_OBJPath(), material.Name + ".png"), ImageFormat.Png);
                    sourceBitmap.Dispose();
                }
                catch (Exception e)
                {
                    Locator.Current.GetService<ILoggerService>().Error(e.Message);
                }
            }
        }

        public void GetResolvedMatrix(IBindable bindable, ref Matrix3D matrix, Dictionary<string, LoadableModel> models)
        {
            matrix.Append(bindable.Matrix);

            if (bindable.BindName != null)
            {
                if (bindable is LoadableModel)
                {
                    if (models.ContainsKey(bindable.BindName))
                    {
                        GetResolvedMatrix(models[bindable.BindName], ref matrix, models);
                    }
                    else if (_slotSets.ContainsKey(bindable.BindName))
                    {
                        if (bindable.SlotName != null && _slotSets[bindable.BindName].Slots.ContainsKey(bindable.SlotName))
                        {
                            var slot = _slotSets[bindable.BindName].Slots[bindable.SlotName];

                            if (Rigs.ContainsKey(_slotSets[bindable.BindName].BindName))
                            {
                                var rigBone = Rigs[_slotSets[bindable.BindName].BindName].Bones.Where(x => x.Name == slot).FirstOrDefault(defaultValue: null);

                                while (rigBone != null)
                                {
                                    matrix.Append(rigBone.Matrix);
                                    rigBone = rigBone.Parent;
                                }
                            }
                        }

                        // not sure this does anything anywhere
                        GetResolvedMatrix(_slotSets[bindable.BindName], ref matrix, models);
                    }
                }

                if (Rigs.ContainsKey(bindable.BindName))
                {
                    GetResolvedMatrix(Rigs[bindable.BindName], ref matrix, models);
                }
            }
        }

        public override ERedDocumentItemType DocumentItemType => ERedDocumentItemType.W2rcBuffer;

        [Reactive] public ImageSource Image { get; set; }

        [Reactive] public object SelectedItem { get; set; }

        [Reactive] public string LoadedModelPath { get; set; }

        [Reactive] public List<LoadableModel> Models { get; set; } = new();

        [Reactive] public Dictionary<string, Rig> Rigs { get; set; } = new();

        [Reactive] public List<Appearance> Appearances { get; set; } = new();

        [Reactive] public Appearance SelectedAppearance { get; set; }

        public static Matrix3D ToMatrix3D(QsTransform qs)
        {
            var matrix = new Matrix3D();
            matrix.Rotate(ToQuaternion(qs.Rotation));
            matrix.Translate(ToVector3D(qs.Translation));
            matrix.Scale(ToScaleVector3D(qs.Scale));
            return matrix;
        }

        public static Matrix3D ToMatrix3D(WorldTransform wt)
        {
            var matrix = new Matrix3D();
            matrix.Rotate(ToQuaternion(wt.Orientation));
            matrix.Translate(ToVector3D(wt.Position));
            return matrix;
        }

        //public static System.Windows.Media.Media3D.Quaternion ToQuaternion(RED4.Types.Quaternion q) => new System.Windows.Media.Media3D.Quaternion(q.I, q.J, q.K, q.R);

        public static System.Windows.Media.Media3D.Quaternion ToQuaternion(RED4.Types.Quaternion q) => new System.Windows.Media.Media3D.Quaternion(q.I, q.K, -q.J, q.R);

        //public static Vector3D ToVector3D(WorldPosition v) => new Vector3D(v.X, v.Y, v.Z);

        //public static Vector3D ToVector3D(Vector4 v) => new Vector3D(v.X, v.Y, v.Z);

        //public static Vector3D ToVector3D(Vector3 v) => new Vector3D(v.X, v.Y, v.Z);

        public static Vector3D ToVector3D(WorldPosition v) => new Vector3D(v.X, v.Z, -v.Y);

        public static Vector3D ToVector3D(Vector4 v) => new Vector3D(v.X, v.Z, -v.Y);

        public static Vector3D ToVector3D(Vector3 v) => new Vector3D(v.X, v.Z, -v.Y);

        public static Vector3D ToScaleVector3D(Vector4 v) => new Vector3D(v.X, v.Z, v.Y);

        public static Vector3D ToScaleVector3D(Vector3 v) => new Vector3D(v.X, v.Z, v.Y);
    }
}
