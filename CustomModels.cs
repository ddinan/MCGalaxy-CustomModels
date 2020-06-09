//reference System.dll
//reference System.Core.dll
//reference System.Collections.dll
//reference Newtonsoft.Json.dll

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCGalaxy.Commands;
using MCGalaxy.Events.PlayerEvents;
using MCGalaxy.Maths;
using MCGalaxy.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MCGalaxy {
    public sealed class CustomModelsPlugin : Plugin {
        public override string name { get { return "CustomModels"; } }
        public override string MCGalaxy_Version { get { return "1.9.2.0"; } }
        public override string creator { get { return "SpiralP & Goodly"; } }

        //------------------------------------------------------------------bbmodel/ccmodel file loading

        const string BlockBenchExt = ".bbmodel";
        const string CCModelExt = ".ccmodel";
        const string CCdirectory = "plugins/models/";
        const string BBdirectory = "plugins/models/bbmodels/";
        const string PersonalCCdirectory = "plugins/personal_models/";
        const string PersonalBBdirectory = "plugins/personal_models/bbmodels/";

        // don't store "name" because we will use filename for model name
        // don't store "parts" because we store those in the full .bbmodel file
        // don't store "u/vScale" because we take it from bbmodel's resolution.width
        class StoredCustomModel {
            public float nameY;
            public float eyeY;
            public Vec3F32 collisionBounds;
            public AABBF32 pickingBoundsAABB;
            public bool bobbing;
            public bool pushes;
            public bool usesHumanSkin;
            public bool calcHumanAnims;
            public bool hideFirstPersonArm;

            public static StoredCustomModel FromCustomModel(CustomModel model) {
                // convert to pixel units
                var storedCustomModel = new StoredCustomModel {
                    nameY = model.nameY * 16.0f,
                    eyeY = model.eyeY * 16.0f,
                    collisionBounds = {
                        X = model.collisionBounds.X * 16.0f,
                        Y = model.collisionBounds.Y * 16.0f,
                        Z = model.collisionBounds.Z * 16.0f,
                    },
                    pickingBoundsAABB = {
                        Min = new Vec3F32 {
                            X = model.pickingBoundsAABB.Min.X * 16.0f,
                            Y = model.pickingBoundsAABB.Min.Y * 16.0f,
                            Z = model.pickingBoundsAABB.Min.Z * 16.0f,
                        },
                        Max = new Vec3F32 {
                            X = model.pickingBoundsAABB.Max.X * 16.0f,
                            Y = model.pickingBoundsAABB.Max.Y * 16.0f,
                            Z = model.pickingBoundsAABB.Max.Z * 16.0f,
                        },
                    },
                    bobbing = model.bobbing,
                    pushes = model.pushes,
                    usesHumanSkin = model.usesHumanSkin,
                    calcHumanAnims = model.calcHumanAnims,
                    hideFirstPersonArm = model.hideFirstPersonArm,
                };
                return storedCustomModel;
            }

            public CustomModel ToCustomModel(string name) {
                string path = GetBBPath(name);
                string contentsBB = File.ReadAllText(path);
                var blockBench = BlockBench.Parse(contentsBB);
                var parts = blockBench.ToCustomModelParts();

                // convert to block units
                var model = new CustomModel {
                    name = name,
                    parts = parts,
                    uScale = blockBench.resolution.width,
                    vScale = blockBench.resolution.height,

                    nameY = this.nameY / 16.0f,
                    eyeY = this.eyeY / 16.0f,
                    collisionBounds = new Vec3F32 {
                        X = this.collisionBounds.X / 16.0f,
                        Y = this.collisionBounds.Y / 16.0f,
                        Z = this.collisionBounds.Z / 16.0f,
                    },
                    pickingBoundsAABB = new AABBF32 {
                        Min = new Vec3F32 {
                            X = this.pickingBoundsAABB.Min.X / 16.0f,
                            Y = this.pickingBoundsAABB.Min.Y / 16.0f,
                            Z = this.pickingBoundsAABB.Min.Z / 16.0f,
                        },
                        Max = new Vec3F32 {
                            X = this.pickingBoundsAABB.Max.X / 16.0f,
                            Y = this.pickingBoundsAABB.Max.Y / 16.0f,
                            Z = this.pickingBoundsAABB.Max.Z / 16.0f,
                        },
                    },
                    bobbing = this.bobbing,
                    pushes = this.pushes,
                    usesHumanSkin = this.usesHumanSkin,
                    calcHumanAnims = this.calcHumanAnims,
                    hideFirstPersonArm = this.hideFirstPersonArm,
                };

                return model;
            }

            public void WriteToFile(string name) {
                string path = GetCCPath(name);
                string storedJsonModel = JsonConvert.SerializeObject(this, Formatting.Indented, jsonSettings);
                File.WriteAllText(path, storedJsonModel);
            }

            public static StoredCustomModel ReadFromFile(string name) {
                string path = GetCCPath(name);
                string contentsCC = File.ReadAllText(path);
                StoredCustomModel storedCustomModel = JsonConvert.DeserializeObject<StoredCustomModel>(contentsCC);
                return storedCustomModel;
            }

            public static bool Exists(string name) {
                string path = GetCCPath(name);
                return File.Exists(path);
            }

            public static string GetCCPath(string name) {
                string path;
                if (name.EndsWith("+")) {
                    path = PersonalCCdirectory + Path.GetFileName(name.ToLower()) + CCModelExt;
                } else {
                    path = CCdirectory + Path.GetFileName(name.ToLower()) + CCModelExt;
                }
                return path;
            }

            public static string GetBBPath(string name) {
                string path;
                if (name.EndsWith("+")) {
                    path = PersonalBBdirectory + Path.GetFileName(name.ToLower()) + BlockBenchExt;
                } else {
                    path = BBdirectory + Path.GetFileName(name.ToLower()) + BlockBenchExt;
                }
                return path;
            }

            public static void WriteBBFile(string name, byte[] bytes) {
                File.WriteAllBytes(
                    GetBBPath(name),
                    bytes
                );
            }
        }


        // returns how many models
        static int CreateMissingCCModels(bool isPersonal) {
            string ccPath;
            string bbPath;
            if (isPersonal) {
                ccPath = PersonalCCdirectory;
                bbPath = PersonalBBdirectory;
            } else {
                ccPath = CCdirectory;
                bbPath = BBdirectory;
            }

            // make sure all cc files are lowercased
            foreach (var entry in new DirectoryInfo(ccPath).GetFiles()) {
                string fileName = entry.Name;
                if (fileName != fileName.ToLower()) {
                    Logger.Log(
                        LogType.SystemActivity,
                        "CustomModels: Renaming {0} to {1}",
                        fileName,
                        fileName.ToLower()
                    );
                    File.Move(
                        ccPath + fileName,
                        ccPath + fileName.ToLower()
                    );
                }
            }

            int count = 0;
            foreach (var entry in new DirectoryInfo(bbPath).GetFiles()) {
                string fileName = entry.Name;
                if (fileName != fileName.ToLower()) {
                    Logger.Log(
                        LogType.SystemActivity,
                        "CustomModels: Renaming {0} to {1}",
                        fileName,
                        fileName.ToLower()
                    );
                    File.Move(
                        bbPath + fileName,
                        bbPath + fileName.ToLower()
                    );
                    fileName = fileName.ToLower();
                }

                string modelName = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);

                if (!extension.CaselessEq(BlockBenchExt)) {
                    continue;
                }

                count += 1;
                if (StoredCustomModel.Exists(modelName)) {
                    continue;
                }

                StoredCustomModel.FromCustomModel(new CustomModel { }).WriteToFile(modelName);

                Logger.Log(
                    LogType.SystemActivity,
                    "CustomModels: Created a new default template \"{0}\" in {1}.",
                    modelName + CCModelExt,
                    ccPath
                );
            }

            return count;
        }

        static void DefineModel(Player p, CustomModel model) {
            if (!p.Supports(CpeExt.CustomModels)) { return; }
            byte[] modelPacket = Packet.DefineModel(model);
            p.Send(modelPacket);
        }

        static void RemoveModel(Player p, string name) {
            if (!p.Supports(CpeExt.CustomModels)) { return; }
            byte[] modelPacket = Packet.RemoveModel(name);
            p.Send(modelPacket);
        }

        static void DefineModelForAllPlayers(CustomModel model) {
            foreach (Player p in PlayerInfo.Online.Items) {
                DefineModel(p, model);
            }
        }

        //------------------------------------------------------------------plugin interface

        CmdCustomModel command = null;
        public override void Load(bool startup) {
            if (!Server.Config.ClassicubeAccountPlus) {
                // sorry but i rely on "+" in filenames! :(
                Logger.Log(
                    LogType.Warning,
                    "CustomModels plugin refusing to load due to Config.ClassicubeAccountPlus not being enabled!",
                    numModels,
                    numPersonalModels
                );
                return;
            }

            command = new CmdCustomModel();
            Command.Register(command);

            OnPlayerConnectEvent.Register(OnPlayerConnect, Priority.Low);
            OnPlayerDisconnectEvent.Register(OnPlayerDisconnect, Priority.Low);

            OnJoiningLevelEvent.Register(OnJoiningLevel, Priority.Low);
            OnJoinedLevelEvent.Register(OnJoinedLevel, Priority.Low);

            OnBeforeChangeModelEvent.Register(OnBeforeChangeModel, Priority.Low);

            if (!Directory.Exists(CCdirectory)) Directory.CreateDirectory(CCdirectory);
            if (!Directory.Exists(BBdirectory)) Directory.CreateDirectory(BBdirectory);
            if (!Directory.Exists(PersonalCCdirectory)) Directory.CreateDirectory(PersonalCCdirectory);
            if (!Directory.Exists(PersonalBBdirectory)) Directory.CreateDirectory(PersonalBBdirectory);


            int numModels = CreateMissingCCModels(false);
            int numPersonalModels = CreateMissingCCModels(true);
            Logger.Log(
                LogType.SystemActivity,
                "CustomModels Loaded with {0} Models and {1} Personal Models",
                numModels,
                numPersonalModels
            );
        }

        public override void Unload(bool shutdown) {
            OnPlayerConnectEvent.Unregister(OnPlayerConnect);
            OnPlayerDisconnectEvent.Unregister(OnPlayerDisconnect);
            OnJoiningLevelEvent.Unregister(OnJoiningLevel);
            OnJoinedLevelEvent.Unregister(OnJoinedLevel);
            OnBeforeChangeModelEvent.Unregister(OnBeforeChangeModel);

            if (command != null) {
                Command.Unregister(command);
                command = null;
            }
        }

        static Dictionary<string, HashSet<string>> SentCustomModels = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static void CheckSendModel(Player p, string modelName) {
            var sentModels = SentCustomModels[p.name];
            if (!sentModels.Contains(modelName)) {
                if (!StoredCustomModel.Exists(modelName)) { return; }
                sentModels.Add(modelName);

                var model = StoredCustomModel.ReadFromFile(modelName).ToCustomModel(modelName);
                DefineModel(p, model);
                p.Message("Defined model %b{0}%S!", modelName);
            }
        }

        static void CheckRemoveModel(Player p, string modelName) {
            var sentModels = SentCustomModels[p.name];
            if (sentModels.Contains(modelName)) {
                sentModels.Remove(modelName);

                RemoveModel(p, modelName);
            }
        }

        // removes all unused models from player, and
        // sends all missing models in level to player
        static void CheckAddRemove(Player p, Level level) {
            var visibleModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            visibleModels.Add(p.Model);

            foreach (Player e in level.getPlayers()) {
                visibleModels.Add(e.Model);
            }
            foreach (PlayerBot e in level.Bots.Items) {
                visibleModels.Add(e.Model);
            }


            var sentModels = SentCustomModels[p.name];
            // clone so we can modify while we iterate
            foreach (var modelName in sentModels.ToArray()) {
                // remove models not found in this level
                if (!visibleModels.Contains(modelName)) {
                    CheckRemoveModel(p, modelName);
                }
            }

            // send new models not yet in player's list
            foreach (var modelName in visibleModels) {
                CheckSendModel(p, modelName);
            }
        }

        static void CheckUpdateAll(string modelName) {
            // re-define the model and do ChangeModel for each entity currently using this model

            // remove this model from everyone's sent list
            foreach (Player p in PlayerInfo.Online.Items) {
                CheckRemoveModel(p, modelName);
            }


            // add this model back to players who see entities using it
            foreach (Player p in PlayerInfo.Online.Items) {
                CheckAddRemove(p, p.level);
            }

            // do ChangeModel on every entity with this model
            // so that we update the model on the client
            var loadedLevels = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            foreach (Player p in PlayerInfo.Online.Items) {
                if (p.Model.CaselessEq(modelName)) {
                    Entities.UpdateModel(p, p.Model);
                }

                if (!loadedLevels.ContainsKey(p.level.name)) {
                    loadedLevels.Add(p.level.name, p.level);
                }
            }
            foreach (var entry in loadedLevels) {
                var level = entry.Value;
                foreach (PlayerBot e in level.Bots.Items) {
                    if (e.Model.CaselessEq(modelName)) {
                        Entities.UpdateModel(e, e.Model);
                    }
                }
            }
        }

        static void OnPlayerConnect(Player p) {
            SentCustomModels.Add(p.name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        static void OnPlayerDisconnect(Player p, string reason) {
            SentCustomModels.Remove(p.name);


            Level prevLevel = p.level;

            if (prevLevel != null) {
                // tell other players still on the last map to remove our model
                // if we were the last one using that model
                foreach (Player e in prevLevel.getPlayers()) {
                    if (e == p) continue;
                    CheckAddRemove(e, prevLevel);
                }
            }
        }

        static void OnJoiningLevel(Player p, Level level, ref bool canJoin) {
            Level prevLevel = p.level;

            // send future/new model list to player
            CheckAddRemove(p, level);
        }

        static void OnJoinedLevel(Player p, Level prevLevel, Level level, ref bool announce) {
            if (prevLevel != null) {
                // tell other players still on the last map to remove our model
                // if we were the last one using that model
                foreach (Player e in prevLevel.getPlayers()) {
                    if (e == p) continue;
                    CheckAddRemove(e, prevLevel);
                }
            }
        }

        static void OnBeforeChangeModel(Player p, byte entityID, string modelName) {
            // use CheckAddRemove because we also want to remove the previous model,
            // if no one else is using it
            CheckAddRemove(p, p.level);
        }

        //------------------------------------------------------------------commands

        class ChatType {
            public Func<CustomModel, string> get;
            // (model, p, input) => bool
            public Func<CustomModel, Player, string[], bool> set;
            public string[] types;

            public ChatType(string type, Func<CustomModel, string> get, Func<CustomModel, Player, string, bool> set) {
                this.types = new string[] { type };
                this.get = get;
                this.set = (model, p, inputs) => {
                    return set(model, p, inputs[0]);
                };
            }

            public ChatType(
                string[] types,
                Func<CustomModel, string> get,
                Func<CustomModel, Player, string[], bool> set
            ) {
                this.types = types;
                this.get = get;
                this.set = set;
            }
        }

        static bool GetRealPixels(Player p, string input, string argName, ref float output) {
            float tmp = 0.0f;
            if (CommandParser.GetReal(p, input, "nameY", ref tmp)) {
                output = tmp / 16.0f;
                return true;
            } else {
                return false;
            }
        }

        static Dictionary<string, ChatType> ModifiableFields = new Dictionary<string, ChatType>(StringComparer.OrdinalIgnoreCase) {
            {
                "nameY",
                new ChatType(
                    "nameY",
                    (model) => "" + model.nameY * 16.0f,
                    (model, p, input) => GetRealPixels(p, input, "nameY", ref model.nameY)
                )
            },
            // {
            //     "eyeY",
            //     new ChatType(
            //         "eyeY",
            //         (model) => "" + model.eyeY * 16.0f,
            //         (model, p, input) => GetRealPixels(p, input, "eyeY", ref model.eyeY)
            //     )
            // },
            // {
            //     "collisionBounds",
            //     new ChatType(
            //         new string[] {"x", "y", "z"},
            //         (model) => {
            //             return string.Format(
            //                 "({0}, {1}, {2})",
            //                 model.collisionBounds.X * 16.0f,
            //                 model.collisionBounds.Y * 16.0f,
            //                 model.collisionBounds.Z * 16.0f
            //             );
            //         },
            //         (model, p, input) => {
            //             if (!GetRealPixels(p, input[0], "x", ref model.collisionBounds.X)) return false;
            //             if (!GetRealPixels(p, input[1], "y", ref model.collisionBounds.Y)) return false;
            //             if (!GetRealPixels(p, input[2], "z", ref model.collisionBounds.Z)) return false;
            //             return true;
            //         }
            //     )
            // },
            // {
            //     "pickingBoundsAABB",
            //     new ChatType(
            //         new string[] {"minX", "minY", "minZ", "maxX", "maxY", "maxZ"},
            //         (model) => {
            //             return string.Format(
            //                 "from ({0}, {1}, {2}) to ({3}, {4}, {5})",
            //                 model.pickingBoundsAABB.Min.X * 16.0f,
            //                 model.pickingBoundsAABB.Min.Y * 16.0f,
            //                 model.pickingBoundsAABB.Min.Z * 16.0f,
            //                 model.pickingBoundsAABB.Max.X * 16.0f,
            //                 model.pickingBoundsAABB.Max.Y * 16.0f,
            //                 model.pickingBoundsAABB.Max.Z * 16.0f
            //             );
            //         },
            //         (model, p, input) => {
            //             if (!GetRealPixels(p, input[0], "minX", ref model.pickingBoundsAABB.Min.X)) return false;
            //             if (!GetRealPixels(p, input[1], "minY", ref model.pickingBoundsAABB.Min.Y)) return false;
            //             if (!GetRealPixels(p, input[2], "minZ", ref model.pickingBoundsAABB.Min.Z)) return false;
            //             if (!GetRealPixels(p, input[3], "maxX", ref model.pickingBoundsAABB.Max.X)) return false;
            //             if (!GetRealPixels(p, input[4], "maxY", ref model.pickingBoundsAABB.Max.Y)) return false;
            //             if (!GetRealPixels(p, input[5], "maxZ", ref model.pickingBoundsAABB.Max.Z)) return false;
            //             return true;
            //         }
            //     )
            // },
            {
                "bobbing",
                new ChatType(
                    "bobbing",
                    (model) => model.bobbing.ToString(),
                    (model, p, input) => CommandParser.GetBool(p, input, ref model.bobbing)
                )
            },
            {
                "pushes",
                new ChatType(
                    "pushes",
                    (model) => model.pushes.ToString(),
                    (model, p, input) => CommandParser.GetBool(p, input, ref model.pushes)
                )
            },
            {
                "usesHumanSkin",
                new ChatType(
                    "usesHumanSkin",
                    (model) => model.usesHumanSkin.ToString(),
                    (model, p, input) => CommandParser.GetBool(p, input, ref model.usesHumanSkin)
                )
            },
            {
                "calcHumanAnims",
                new ChatType(
                    "calcHumanAnims",
                    (model) => model.calcHumanAnims.ToString(),
                    (model, p, input) => CommandParser.GetBool(p, input, ref model.calcHumanAnims)
                )
            },
            {
                "hideFirstPersonArm",
                new ChatType(
                    "hideFirstPersonArm",
                    (model) => model.hideFirstPersonArm.ToString(),
                    (model, p, input) => CommandParser.GetBool(p, input, ref model.hideFirstPersonArm)
                )
            },
        };

        class CmdCustomModel : Command {
            public override string name { get { return "CustomModel"; } }
            public override string shortcut { get { return "cm"; } }
            public override string type { get { return CommandTypes.Other; } }

            public override void Help(Player p) {
                p.Message("%T/CustomModel upload [bbmodel url] %H- Upload a BlockBench file to use as your personal model.");
                p.Message("%T/CustomModel config [model] [field] [value] %H- Configures options on your personal model.");
                // TODO make fields above have help and let "/help CustomModel fields" show them

                // p.Message("%HUse %T/Help CustomModel models %Hfor a list of models.");
                // p.Message("%HUse %T/Help CustomModel scale %Hfor how to scale a model.");

            }

            public override void Help(Player p, string message) {
                // if (message.CaselessEq("models")) {
                //     p.Message("%HAvailable models: %SChibi, Chicken, Creeper, Giant, Humanoid, Pig, Sheep, Spider, Skeleton, Zombie, Head, Sit, Corpse");
                //     p.Message("%HTo set a block model, use a block ID for the model name.");
                //     p.Message("%HUse %T/Help CustomModel scale %Hfor how to scale a model.");
                // } else if (message.CaselessEq("scale")) {
                //     p.Message("%HFor a scaled model, put \"|[scale]\" after the model name.");
                //     p.Message("%H  e.g. pig|0.5, chibi|3");
                //     p.Message("%HUse X/Y/Z [scale] for [model] to set scale on one axis.");
                //     p.Message("%H  e.g. to set twice as tall, use 'Y 2' for [model]");
                //     p.Message("%H  Use a [scale] of 0 to reset");
                // } else {
                Help(p);
            }

            public override void Use(Player p, string message) {
                var modelName = Path.GetFileName(p.name);

                var words = message.SplitSpaces();
                if (words.Length >= 1) {
                    // /CustomModel config
                    if (words[0].CaselessEq("config")) {

                        if (!StoredCustomModel.Exists(modelName)) {
                            p.Message("%WCustom Model %S{0} %Wnot found", modelName);
                            return;
                        }

                        CustomModel model = StoredCustomModel.ReadFromFile(modelName).ToCustomModel(modelName);
                        if (words.Length == 1) {
                            // /CustomModel config
                            foreach (var entry in ModifiableFields) {
                                var fieldName = entry.Key;
                                var chatType = entry.Value;
                                p.Message(
                                    "{0} = %T{1}",
                                    fieldName,
                                    chatType.get.Invoke(model)
                                );
                            }
                            return;
                        } else if (words.Length >= 2) {
                            // /CustomModel config [field]
                            // or
                            // /CustomModel config [field] [value]
                            var fieldName = words[1];
                            if (!ModifiableFields.ContainsKey(fieldName)) {
                                p.Message(
                                    "%WNo such field %S{0}",
                                    fieldName
                                );
                                return;
                            }

                            var chatType = ModifiableFields[fieldName];
                            if (words.Length == 2) {
                                // /CustomModel config [field]
                                p.Message(
                                    "{0} = %T{1}",
                                    fieldName,
                                    chatType.get.Invoke(model)
                                );
                                return;
                            }

                            var inputs = words.Skip(2).ToArray();
                            if (inputs.Length != chatType.types.Length) {
                                p.Message(
                                    "%WNot enough args for setting field %S{0}",
                                    fieldName
                                );
                                return;
                            }

                            if (chatType.set.Invoke(model, p, inputs)) {
                                // field was set, update file!

                                StoredCustomModel.FromCustomModel(model).WriteToFile(modelName);
                                CheckUpdateAll(modelName);
                            }
                            return;
                        }
                    } else if (words[0].CaselessEq("upload")) {
                        // /CustomModel upload
                        // upload a personal bbmodel with a +

                        if (words.Length == 2) {
                            // /CustomModel upload [url]
                            var url = words[1];
                            var bytes = HttpUtil.DownloadData(url, p);
                            if (bytes != null) {
                                StoredCustomModel.WriteBBFile(modelName, bytes);

                                if (!StoredCustomModel.Exists(modelName)) {
                                    // create a default ccmodel file if doesn't exist
                                    StoredCustomModel.FromCustomModel(new CustomModel { }).WriteToFile(modelName);
                                }

                                // attempt to load it so the user sees the error if there is one
                                StoredCustomModel.ReadFromFile(modelName).ToCustomModel(modelName);

                                CheckUpdateAll(modelName);
                                p.Message(
                                    "%TModel %S{0}%T updated!",
                                    modelName
                                );
                            }
                            return;
                        }
                    }
                }

                Help(p);
            }
        }

        //------------------------------------------------------------------bbmodel json parsing

        class WritablePropertiesOnlyResolver : DefaultContractResolver {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization) {
                IList<JsonProperty> props = base.CreateProperties(type, memberSerialization);
                // Writable because Vec3F32 has some "getter-only" fields we don't want to serialize
                return props.Where(p => p.Writable).ToList();
            }
        }

        static JsonSerializerSettings jsonSettings = new JsonSerializerSettings {
            ContractResolver = new WritablePropertiesOnlyResolver()
        };

#pragma warning disable 0649
        class BlockBench {
            public class JsonRoot {
                public Meta meta;
                public string name;
                public Element[] elements;
                public Resolution resolution;

                public string ToJson() {
                    return JsonConvert.SerializeObject(this);
                }

                public CustomModelPart[] ToCustomModelParts() {
                    var list = new List<CustomModelPart>();

                    if (!this.meta.box_uv) {
                        throw new Exception("unimplemented: not using box_uv");
                    }

                    bool notifiedTexture = false;
                    foreach (Element e in this.elements) {
                        if (e.visibility.HasValue && e.visibility.Value == false) {
                            continue;
                        }

                        if (!notifiedTexture &&
                            (!e.faces.north.texture.HasValue ||
                                !e.faces.east.texture.HasValue ||
                                !e.faces.south.texture.HasValue ||
                                !e.faces.west.texture.HasValue ||
                                !e.faces.up.texture.HasValue ||
                                !e.faces.down.texture.HasValue
                            )
                        ) {
                            Logger.Log(
                                LogType.Warning,
                                "Warning: Blockbench Model '" +
                                this.name +
                                "' has one or more faces with no texture!"
                            );
                            notifiedTexture = true;
                        }

                        UInt16 texX = 0;
                        UInt16 texY = 0;
                        if (e.uv_offset != null) {
                            texX = e.uv_offset[0];
                            texY = e.uv_offset[1];
                        }

                        Vec3F32 rotation = new Vec3F32 { X = 0, Y = 0, Z = 0 };
                        if (e.rotation != null) {
                            rotation = new Vec3F32 {
                                X = e.rotation[0],
                                Y = e.rotation[1],
                                Z = e.rotation[2],
                            };
                        }

                        Vec3F32 v1 = new Vec3F32 {
                            X = e.from[0] - e.inflate,
                            Y = e.from[1] - e.inflate,
                            Z = e.from[2] - e.inflate,
                        };
                        Vec3F32 v2 = new Vec3F32 {
                            X = e.to[0] + e.inflate,
                            Y = e.to[1] + e.inflate,
                            Z = e.to[2] + e.inflate,
                        };

                        if (e.shade.HasValue && e.shade.Value == false) {
                            // mirroring enabled, flip X's
                            float tmp = v1.X;
                            v1.X = v2.X;
                            v2.X = tmp;
                        }

                        var part = new CustomModelPart {
                            boxDesc = new BoxDesc {
                                texX = texX,
                                texY = texY,
                                sizeX = (byte)Math.Abs(e.faces.up.uv[2] - e.faces.up.uv[0]),
                                sizeY = (byte)Math.Abs(e.faces.east.uv[3] - e.faces.east.uv[1]),
                                sizeZ = (byte)Math.Abs(e.faces.east.uv[2] - e.faces.east.uv[0]),
                                x1 = v1.X / 16.0f,
                                y1 = v1.Y / 16.0f,
                                z1 = v1.Z / 16.0f,
                                x2 = v2.X / 16.0f,
                                y2 = v2.Y / 16.0f,
                                z2 = v2.Z / 16.0f,
                                rotX = e.origin[0] / 16.0f,
                                rotY = e.origin[1] / 16.0f,
                                rotZ = e.origin[2] / 16.0f,
                            },
                            rotation = rotation,
                            anim = CustomModelAnim.None,
                            fullbright = false,
                        };

                        foreach (var attr in e.name.SplitComma()) {

                            var animModifier = 1.0f;
                            var colonSplit = attr.Split(':');
                            if (colonSplit.Length >= 2) {
                                animModifier = float.Parse(colonSplit[1]);
                            }

                            if (attr.CaselessStarts("head")) {
                                part.anim = CustomModelAnim.Head;
                            } else if (attr.CaselessStarts("leftleg")) {
                                part.anim = CustomModelAnim.LeftLeg;
                            } else if (attr.CaselessStarts("rightleg")) {
                                part.anim = CustomModelAnim.RightLeg;
                            } else if (attr.CaselessStarts("leftarm")) {
                                part.anim = CustomModelAnim.LeftArm;
                            } else if (attr.CaselessStarts("rightarm")) {
                                part.anim = CustomModelAnim.RightArm;
                            } else if (attr.CaselessStarts("spinxvelocity")) {
                                part.anim = CustomModelAnim.SpinXVelocity;
                            } else if (attr.CaselessStarts("spinyvelocity")) {
                                part.anim = CustomModelAnim.SpinYVelocity;
                            } else if (attr.CaselessStarts("spinzvelocity")) {
                                part.anim = CustomModelAnim.SpinZVelocity;
                            } else if (attr.CaselessStarts("spinx")) {
                                part.anim = CustomModelAnim.SpinX;
                            } else if (attr.CaselessStarts("spiny")) {
                                part.anim = CustomModelAnim.SpinY;
                            } else if (attr.CaselessStarts("spinz")) {
                                part.anim = CustomModelAnim.SpinZ;
                            }

                            part.animModifier = animModifier;
                        }

                        if (e.name.CaselessContains("fullbright")) {
                            part.fullbright = true;
                        }

                        list.Add(part);

                    }

                    return list.ToArray();
                }

                public class Resolution {
                    public UInt16 width;
                    public UInt16 height;
                }
                public class Meta {
                    public bool box_uv;
                }
                public class Element {
                    public string name;
                    // 3 numbers
                    public float[] from;
                    // 3 numbers
                    public float[] to;

                    public bool? visibility;

                    // if set to 1, uses a default png with some colors on it,
                    // we will only support skin pngs, so maybe notify user?
                    public UInt16 autouv;

                    // optional
                    public float inflate;

                    // if false, mirroring is enabled
                    // if null, mirroring is disabled
                    public bool? shade;

                    // so far only false?
                    // public locked: bool;
                    // optional, 3 numbers
                    public float[] rotation;

                    /// "Pivot Point"
                    // 3 numbers
                    public float[] origin;

                    // optional, 2 numbers
                    public UInt16[] uv_offset;
                    public Faces faces;
                }
                public class Faces {
                    public Face north;
                    public Face east;
                    public Face south;
                    public Face west;
                    public Face up;
                    public Face down;
                }
                public class Face {
                    // 4 numbers
                    public UInt16[] uv;
                    public UInt16? texture;
                }
            }

            public static JsonRoot Parse(string json) {
                JsonRoot m = JsonConvert.DeserializeObject<JsonRoot>(json);
                return m;
            }
        }
#pragma warning restore 0649

    }

}
