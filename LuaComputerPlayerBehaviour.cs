namespace LouveSystems.K2.BotLib
{
    using LouveSystems.K2.Lib;
    using MoonSharp.Interpreter;
    using MoonSharp.Interpreter.Loaders;
    using MoonSharp.Interpreter.Serialization;
    using MoonSharp.Interpreter.Serialization.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Text;

    public class LuaComputerPlayerBehaviour : ComputerPlayerBehaviour
    {
        public const byte NO_AUTO_YIELD = 0;
        public const byte AUTO_YIELD_EVERY_X_INSTRUCTION = 60;

        private struct LuaScriptLoader : IScriptLoader
        {
            private readonly IReadOnlyList<LuaFile> files;

            public LuaScriptLoader(IReadOnlyList<LuaFile> files)
            {
                this.files = files;
            }

            public object LoadFile(string file, Table globalContext)
            {
                for (int i = 0; i < files.Count; i++) {
                    if (files[i].name == file) {
                        return files[i].getText();
                    }
                }

                return null;
            }

            public string ResolveFileName(string filename, Table globalContext)
            {
                return filename;
            }

            public string ResolveModuleName(string modname, Table globalContext)
            {

                for (int i = 0; i < files.Count; i++) {
                    if (files[i].name == modname) {
                        return modname;
                    }
                }

                return null;
            }
        }

        protected delegate void NoParamNoReturnDelegate();
        protected delegate void OneParamNoReturnDelegate(DynValue word);
        protected delegate void TwoParamNoReturnDelegate(DynValue a, DynValue b);
        protected delegate void ThreeParamNoReturnDelegate(DynValue a, DynValue b, DynValue c);
        protected delegate void OneFloatParamNoReturnDelegate(float x);
        protected delegate DynValue GetterDelegate();
        protected delegate DynValue[] MultiGetterDelegate();
        protected delegate DynValue OneParamGetterDelegate(DynValue a);
        protected delegate DynValue[] OneParamMultiGetterDelegate(DynValue a);
        protected delegate DynValue TwoParamGetterDelegate(DynValue a, DynValue b);
        protected delegate DynValue ThreeParamGetterDelegate(DynValue a, DynValue b, DynValue c);

        public class MissingScriptException : System.Exception { public MissingScriptException(string str) : base(str) { } }
        public class ScriptErrorException : System.Exception { public ScriptErrorException(string str) : base(str) { } }
        public class MissingCriticalFunction : System.Exception
        {
            public List<string> MissingFunctions;
            public MissingCriticalFunction(List<string> missingFunctions) : base()
            {
                this.MissingFunctions = missingFunctions;
            }

            public override string ToString()
            {
                return $"{nameof(MissingCriticalFunction)} => {string.Join(" ", MissingFunctions)}";
            }
        }

        private const string TAKE_ACTIONS_FUNC = "PLAY_TURN";
        private const string TAKE_ACTIONS_LATE_FUNC = "PLAY_TURN_LATE";
        private const string GET_PERSONAS_FUNC = "GET_PERSONAS";

        private readonly Dictionary<string, string> requirableFiles = new Dictionary<string, string>();

        private readonly string name;

        private readonly MoonSharp.Interpreter.Script script;

        private readonly string[] obligatoryFunctions = new[]
        {
            TAKE_ACTIONS_FUNC,
            GET_PERSONAS_FUNC
        };

        private readonly Dictionary<byte, List<Coroutine>> routines = new Dictionary<byte, List<Coroutine>>();

        private readonly List<ComputerPersona> personas = new List<ComputerPersona>();

        public LuaComputerPlayerBehaviour(int fileIndex, IReadOnlyList<LuaFile> allFiles)
        {
            name = Path.GetFileNameWithoutExtension(allFiles[fileIndex].name);
            script = new MoonSharp.Interpreter.Script(
                CoreModules.Preset_HardSandbox
                | CoreModules.LoadMethods
                | CoreModules.Coroutine
                | CoreModules.Metatables
            );

            script.Options.ScriptLoader = new LuaScriptLoader(allFiles);

            InjectGlobals();

            try {
                script.DoString(allFiles[fileIndex].getText(), codeFriendlyName: allFiles[fileIndex].name);
            }
            catch (MoonSharp.Interpreter.InterpreterException e) {

                StringBuilder sb = new StringBuilder();

                sb.AppendLine(e.DecoratedMessage);

                sb.AppendLine($"While loading script named {(allFiles.Count > fileIndex ? allFiles[fileIndex].name : "<OOR>")}");
                sb.AppendLine($"While loading script content {(allFiles.Count > fileIndex ? allFiles[fileIndex].getText() : "<OOR>")}");
                sb.AppendLine($"All files: {string.Join(" ", allFiles.Select(o => o.name))}");
                sb.AppendLine($"All texts: {string.Join(" ", allFiles.Select(o => o.getText()))}");

                Err(sb.ToString());

                throw e;
            }

            CheckScriptValidity();

            GeneratePersonas();
        }

        public override IEnumerator<bool> TakeActionsLate(byte forPlayerIndex, GameSession session)
        {
            if (script.Globals.Get(TAKE_ACTIONS_LATE_FUNC).IsNilOrNan()) {
                if (routines.ContainsKey(forPlayerIndex)) {
                    routines[forPlayerIndex].Clear();
                }
                else {
                    routines.Add(forPlayerIndex, new List<Coroutine>());
                }

                DynValue api = MakeGameObject(forPlayerIndex, session);
                api.Table.Set(PascalToSnake("localPlayerIndex"), DynValue.NewNumber(forPlayerIndex));

                try {
                    Execute(forPlayerIndex, TAKE_ACTIONS_LATE_FUNC, api);
                }
                catch (Exception e) {
                    Err(e.ToString());
                    throw e;
                }

                var it = PlayRoutinesToEnd(forPlayerIndex, MAX_TICKS_LATE_ACTIONS);
                bool shouldEnd = it.Current;
                while (it.MoveNext() && !shouldEnd) {
                    shouldEnd = it.Current;
                    yield return false;
                }
            }

            yield return true;
        }

        public override IEnumerator<bool> TakeActions(byte forPlayerIndex, GameSession session)
        {
            if (routines.ContainsKey(forPlayerIndex)) {
                routines[forPlayerIndex].Clear();
            }
            else {
                routines.Add(forPlayerIndex, new List<Coroutine>());
            }

            DynValue api = MakeGameObject(forPlayerIndex, session);
            api.Table.Set(PascalToSnake("localPlayerIndex"), DynValue.NewNumber(forPlayerIndex));

            int maxTicks = AUTO_YIELD_EVERY_X_INSTRUCTION == NO_AUTO_YIELD ?
                MAX_TICKS_ACTIONS :
                int.MaxValue;

            try {
                Execute(forPlayerIndex, TAKE_ACTIONS_FUNC, api);
                return PlayRoutinesToEnd(forPlayerIndex, maxTicks);
            }
            catch (InterpreterException iE) {
                Err(iE.DecoratedMessage);
                Err(iE.ToString());
                throw iE;
            }
            catch (Exception e) {
                Err(e.ToString());
                throw e;
            }
        }

        private IEnumerator<bool> PlayRoutinesToEnd(byte forPlayerIndex, int max)
        {
            int i = 0;
            while (true) {
                for (int routineIndex = 0; routineIndex < routines[forPlayerIndex].Count; routineIndex++) {
                    var routine = routines[forPlayerIndex][routineIndex];
                    try {
                        routine.Resume();
                    }
                    catch (InterpreterException iE) {
                        Err(iE.DecoratedMessage);
                        Err(iE.ToString());
                        break;
                    }
                    catch (Exception e) {
                        Err(e.ToString());
                        break;
                    }

                    if (routine.State == CoroutineState.Dead) {
                        routines[forPlayerIndex].Remove(routine);
                        routineIndex--;
                    }

                    i++;

                    if (i >= max) {
                        Log($"NOTICE: Max yields reached (>{max}) for player {forPlayerIndex}, interrupting by force");
                        break;
                    }
                }

                if (routines[forPlayerIndex].Count == 0) {
                    break;
                }

                if (i >= max) {
                    Log($"NOTICE: Max yields reached (>{max}) for player {forPlayerIndex}, interrupting by force");
                    break;
                }

                yield return false;
            }

            routines[forPlayerIndex].Clear();
            yield return true;
        }

        private void Execute(byte playerIndex, string functionName, params object[] args)
        {
            if (!script.Globals.Get(functionName).IsNilOrNan()) {
                var routine = script.CreateCoroutine(script.Globals[functionName]).Coroutine;
                routine.AutoYieldCounter = AUTO_YIELD_EVERY_X_INSTRUCTION;

                try {
                    routine.Resume(args);
                }
                catch (InterpreterException e) {
                    Err(e.DecoratedMessage);
                }

                if (routine.State != CoroutineState.Dead) {
                    // We're not finished!
                    routines[playerIndex].Add(routine);
                }
            }
        }

        private void PrintTableRecursively(DynValue val, StringBuilder builder, int depth = 0)
        {
            string tabs = "";

            for (int i = 0; i < depth; i++) {
                tabs += "\t";
            }

            foreach (var key in val.Table.Keys) {

                var value = val.Table.Get(key);

                builder.Append(tabs);
                builder.Append(key.ToString());
                builder.Append(" = ");

                if (value.Type == DataType.Table) {
                    builder.AppendLine("{");
                    PrintTableRecursively(value, builder, depth + 1);
                    builder.Append(tabs + "}");
                }
                else if (value.Type == DataType.Function) {
                    builder.Append("void ()");
                }
                else if (value.Type == DataType.ClrFunction) {

                    builder.Append(value.Callback.Name + " ()");
                }
                else {
                    builder.Append(value.ToDebugPrintString());
                }

                builder.Append(",");
                builder.AppendLine();
            }
        }

        protected DynValue MakeGameObject(byte forPlayerIndex, GameSession session)
        {
            // This is repeated at each decision time
            Table table = new Table(script);

            WriteGameObjectInto(forPlayerIndex, session, table);

            return DynValue.NewTable(table);
        }

        private void WriteGameObjectInto(byte forPlayerIndex, GameSession session, Table table)
        {
            table["rules"] = MakeRulesObject(session.Rules);
            table["random"] = MakeAPI(session.ComputersRandom);
            table["player"] = MakeAPI(session.SessionPlayers[forPlayerIndex]);
            table["world"] = MakeWorldObject(session.CurrentGameState.world, session.Rules, session.SessionPlayers[forPlayerIndex].RealmIndex);
            table["voting"] = MakeVotingObject(session.CurrentGameState.voting);
            table["days_passed"] = session.CurrentGameState.daysPassed;
            table["days_before_next_council"] = session.CurrentGameState.daysRemainingBeforeNextCouncil;
            table["councils_passed"] = session.CurrentGameState.councilsPassed;
            table["buildings"] = MakeBuildingsObject(session.SessionPlayers[forPlayerIndex], session.CurrentGameState.world, session);

            table["refresh"] = (OneParamNoReturnDelegate)(DynValue self) => {
                WriteGameObjectInto(forPlayerIndex, session, self.Table);
            };

        }

        private void WriteRealmObjectInto(SessionPlayer player, byte forRealmIndex, World world, GameSession session, Table table)
        {
            
            table["capital"] = 
                world.GetCapitalOfRealm(forRealmIndex, out int regionIndex) 
                ? Dyn(regionIndex) 
                : DynValue.Nil;
            
            {
                List<DynValue> regionsIndices = new ();

                List<int> regions = new List<int>();

                world.GetTerritoryOfRealm(forRealmIndex, regions);

                for (int i = 0; i < regions.Count; i++) {
                    regionsIndices.Add(Dyn(regions[i]));
                }
                
                table["owned_regions"] = new Table(script, regionsIndices);
            }

            table["is_council"] = world.IsCouncilRealm(forRealmIndex);

            table["faction"] = world.GetRealmFaction(forRealmIndex);

            if (world.IsRealmAlliedWith(player.RealmIndex, forRealmIndex) && session.GetOwnerOfRealm(forRealmIndex, out byte playerId))
            {
                SessionPlayer forPlayer = session.SessionPlayers[playerId];
                table["administration_upgrade_is_planned"] = forPlayer.AdminUpgradeIsPlanned();
                table["any_decisions_remaining"] = forPlayer.AnyDecisionsRemaining();
                table["can_pay_for_favours"] = forPlayer.CanPayForFavours();
                table["can_upgrade_administration"] = forPlayer.CanUpgradeAdministration();
                table["administration_upgrade_silver_cost"] = forPlayer.GetAdministrationUpgradeSilverCost();
                table["max_decisions"] = forPlayer.GetMaximumDecisions();
                table["remaining_decisions"] = forPlayer.GetRemainingDecisions();
                table["silver_treasury"] = forPlayer.GetTreasury();
                table["is_favoured"] = forPlayer.IsFavoured();
            }

            if (world.CanSeePlannedAttacksOf(player.RealmIndex, forRealmIndex))
            {
                
                DynValue[] getPlannedAttacks()
                {
                    List<RegionAttackRegionTransform> planned = new List<RegionAttackRegionTransform>();
                    if (player.GetPlannedAttacks(planned)) {
                        Table[] tables = new Table[planned.Count];
                        DynValue[] values = new DynValue[tables.Length];
                        for (int i = 0; i < planned.Count; i++) {
                            tables[i] = new Table(script);
                            var attack = planned[i];

                            tables[i].Set(PascalToSnake("AttackingRegionIndex"), DynValue.NewNumber(attack.AttackingRegionIndex));
                            tables[i].Set(PascalToSnake("TargetRegionIndex"), DynValue.NewNumber(attack.targetRegionIndex));
                            tables[i].Set(PascalToSnake("Extended"), DynValue.NewBoolean(attack.isExtendedAttack));

                            values[i] = DynValue.NewTable(table);
                        }


                        return values;
                    }
                }
                
                table["planned_attacks"] = getPlannedAttacks();
                
                table["any_attack_planned"] = table["planned_attacks"].Table.Length;
            }

            if (world.CanSeePlannedConstructionsOf(player.RealmIndex, forRealmIndex))
            {

                DynValue[] getPlannedBuildings()
                {
                    List<EBuilding> planned = new List<EBuilding>();
                    if (player.GetPlannedConstructions(planned)) {
                        return planned.Select(o => DynValue.FromObject(script, o)).ToArray();
                    }

                    return new DynValue[0];
                }

                table["planned_buildings"] = getPlannedBuildings();
                table["is_building_anything"] = table["planned_buildings"].Table.Length;

            }

            
            table["refresh"] = (OneParamNoReturnDelegate)(DynValue self) => {
                WriteRealmObjectInto(player, forRealmIndex, world, session, self.Table);
            };
        }

        private DynValue MakeRealmObject(SessionPlayer player, byte forRealmIndex, World world, GameSession session)
        {
            Table table = new Table(script);
            
            WriteRealmObjectInto(player, forRealmIndex, world, session, table);

            return DynValue.NewTable(table);
        }
        
        private DynValue MakeBuildingsObject(SessionPlayer player, World world, GameSession session)
        {
            Table table = new Table(script);
            
            WriteBuildingsObjectInto(player, world, session, table);

            return DynValue.NewTable(table);
        }


        private void WriteBuildingObjectInto(SessionPlayer player, EBuilding building, World world, GameSession session, Table table)
        {
            table["can_afford"] = player.CanAfford(building);
            table[PascalToSnake(nameof(player.CanBuild))] = ToLuaFunction<int, EBuilding, bool>(player.CanBuild);
        }

        private void WriteWorldObjectInto(SessionPlayer player, World world, GameSession session, Table table)
        {
            EFactionFlag faction = player.Faction;
            
            {
                List<DynValue> regionObjects = new (world.Regions.Count);
                for (int i = 0; i < world.Regions.Count; i++) {
                    if (!world.Regions[i].inert) {
                        regionObjects.Add(MakeRegionObject(player, i, world, session));
                    }
                }

                Table regions = new Table(script, regionObjects.ToArray());
                table["regions"] = regions;
            }

            {
                List<DynValue> realmObjects = new (world.Regions.Count);
                for (byte i = 0; i < world.Realms.Count; i++) {
                    realmObjects.Add(MakeRealmObject(player, i, world, session));
                }

                Table realms = new Table(script, realmObjects.ToArray());
                table["realms"] = realms;
            }
            
            table["refresh"] = (OneParamNoReturnDelegate)(DynValue self) => {
                WriteWorldObjectInto(player, world, session, self.Table);
            };
        }

        private DynValue MakeWorldObject(SessionPlayer player, World world, GameSession session)
        {
            Table table = new Table(script);
            WriteWorldObjectInto(player, world, session);
            return DynValue.NewTable(table);
        }

        private void WriteRegionObjectInto(SessionPlayer player, int forRegionIndex, World world, GameSession session, Table table)
        {

            EFactionFlag faction = player.Faction;
            Position position = world.Position((int)forRegionIndex);

           List<int> neighbors = new List<int>(8);
           world.GetNeighboringRegions(forRegionIndex, neighbors);

           table["neighbor_indices"] = new Table(script, neighbors.Select(o => DynValue.NewNumber(o)).ToArray());
           table["building"] = world.Regions[forRegionIndex].buildings;
            
           table["planned_construction"] = 
                session.GetPlannedConstructionForRegion(forRegionIndex, out SessionPlayer builder, out EBuilding building)
                && player.CanSeePlannedConstructionsOf(builder) 
                    ? building 
                    : EBuilding.None;

            if (world.regions[forRegionIndex].IsOwned(byte ownerIndex))
            {
                List<RegionAttackRegionTransform> attacks = new ();
                if (session.GetOwnerOfRealm(ownerIndex, out byte owningPlayerId))
                {
                    SessionPlayer otherPlayer = session.SessionPlayers[owningPlayerId];
                    if (player.CanSeePlannedAttacksOf(otherPlayer))
                    {
                        otherPlayer.GetPlannedAttacks(attacks);
                    }
                }
                
                table["planned_attacks"] = new Table(script, attacks.Select(o=>Dyn(o.targetRegionIndex)).ToArray());
            }
            
            table["has_played"] = session.HasRegionPlayed(forRegionIndex, out RegionRelatedTransform t)
                && player.CanSeePlannedAttacksOf(t.owningRealm);

            table["owner"] = world.Regions[forRegionIndex].GetOwner(out byte realmIndex) ? realmIndex : DynValue.Nil;
            table["subjugation_owner"] = world.Realms[player.RealmIndex].IsSubjugated(out byte subjugatorRealm)
                ? subjugatorRealm
                : table["owner"];  

            table["silver_revenue"] = world.GetRegionSilverWorth(forRegionIndex);

            table["is_vulnerable"] = false;
            {
                List<int> cache = new List<int>();
                for (int i = 0; i < neighbors.Count; i++)
                {
                    cache.Clear();
                    EFactionFlag neighborFaction = world.GetRegionFaction(neighbors[i]);
                    bool canExtend = neighborFaction.HasFlagSafe(EFactionFlag.Charge);
                    world.GetAttackTargetsForRegionNoAlloc(neighbors[i], canExtend, cache);
                    cache.RemoveAll(o=> world.Regions[o].CannotBeTaken(session.Rules, neighborFaction))
                
                    if (cache.Count > 0)
                    {
                        table["is_vulnerable"] = true;
                        break;
                    }
                }
            }    

            table["potential_attack_targets"] = new Table(
                script, 
                world
                    .GetAttackTargetsForRegion(neighbors[i], faction.HasFlagSafe(EFactionFlag.Charge))
                    .ToArray()
            );

            table["lootable_silver"] = world.GetRegionLootableSilverWorth(forRegionIndex, player.RealmIndex);

            table["position"] = new Table(script,  new DynValue[] {
                DynValue.NewNumber(position.x),
                DynValue.NewNumber(position.y)
            });

            table["is_reinforced_against_attacks"] = world.Regions[forRegionIndex].IsReinforcedAgainstAttack(session.Rules, faction);

            table["faction"] = faction;

            table["can_be_taken"] = !world.Regions[forRegionIndex].CannotBeTaken(session.Rules, faction);

            table["can_be_attacked"] = world.CanRealmAttackRegion(player.RealmIndex, forRegionIndex);

            {
                List<DynValue> attackAngles = new (world.Realms.Count);

                for(int i = 0; i < neighbors.Count; i++)
                {  
                    int neighborIndex = neighbors[i];
                    if (world.Regions[neighborIndex].IsOwned(out byte owningRealm))
                    {
                        if (world.CanRealmAttackRegion(owningRealm, forRegionIndex))
                        {
                            attackAngles.Add(Dyn(owningRealm));
                        }
                    }
                }

                Table potential_attackers = new Table(script, regionObjects.ToArray());
                table["potential_attacking_regions"] = potential_attackers;
            }

            table["is_council"] = world.IsCouncilRegion(forRegionIndex);

            table["refresh"] = (OneParamNoReturnDelegate)(DynValue self) => {
                WriteRegionObjectInto(player, forRegionIndex, world, session, self.Table);
            };

            table["can_play"] = world.IsActionableRegion(player.RealmIndex, forRegionIndex);
        }

        private DynValue MakeRegionObject(SessionPlayer player, int forRegionIndex, World world, GameSession session)
        {
            Table table = new Table(script);
            WriteRegionObjectInto(player, forRegionIndex, world, session, table);
           return DynValue.NewTable(table);
        }


        private DynValue MakeVotingObject(Voting voting)
        {
            Table table = new Table(script);

            table[PascalToSnake(nameof(voting.Result.wastedVotes))] = voting.Result.wastedVotes;

            {

                if (voting.Result.scores == null) {
                    table[PascalToSnake(nameof(voting.Result.scores))] = DynValue.Nil;
                }
                else {

                    Table results = new Table(script);
                    for (int i = 0; i < voting.Result.scores.Length; i++) {
                        Table data = new Table(script);
                        data.Set("total", Dyn(voting.Result.scores[i].totalVotes));

                        for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {
                            data.Set($"has_{PascalToSnake(criteria.ToString())}", Dyn(voting.Result.scores[i].wonCriterias.Contains(criteria)));
                        }

                        results.Set(voting.Result.scores[i].realmIndex, DynValue.NewTable(data));

                        table[PascalToSnake(nameof(voting.Result.scores))] = results;
                    }
                }
            }

            return DynValue.NewTable(table);
        }

        private DynValue Dyn<T>(T obj)
        {
            return DynValue.FromObject(script, obj);
        }

        private DynValue[] Dyn<T>(IReadOnlyList<T> obj)
        {
            DynValue[] arr = new DynValue[obj.Count];
            for (int i = 0; i < obj.Count; i++) {
                arr[i] = DynValue.FromObject(script, obj[i]);
            }

            return arr;
        }

        private TwoParamGetterDelegate ToLuaFunction<T1, T2, TResult>(Func<T1, T2, TResult> func)
        {
            return (DynValue val1, DynValue val2) => DynValue.FromObject(script, func(val1.ToObject<T1>(), val2.ToObject<T2>()));
        }

        private OneParamGetterDelegate ToLuaFunction<T, TResult>(Func<T, TResult> func)
        {
            return (DynValue val) => DynValue.FromObject(script, func(val.ToObject<T>()));
        }

        private TwoParamNoReturnDelegate ToLuaFunction<T1, T2>(Action<T1, T2> action)
        {
            return (DynValue val1, DynValue val2) => action(val1.ToObject<T1>(), val2.ToObject<T2>());
        }


        private OneParamNoReturnDelegate ToLuaFunction<T>(Action<T> action)
        {
            return (DynValue val) => action(val.ToObject<T>());
        }

        private NoParamNoReturnDelegate ToLuaFunction(Action action)
        {
            return () => action();
        }

        private GetterDelegate ToLuaFunction<T>(Func<T> func)
        {
            return () => DynValue.FromObject(script, func());
        }

        protected DynValue MakeAPI(SessionPlayer player)
        {
            Table table = new Table(script);

            table[PascalToSnake(nameof(player.PayForFavours))] = ToLuaFunction(player.PayForFavours);
            table[PascalToSnake(nameof(player.UpgradeAdministration))] = ToLuaFunction(player.UpgradeAdministration);
            table[PascalToSnake(nameof(player.PlanAttack))] = ToLuaFunction<int, int>(player.PlanAttack);
            table[PascalToSnake(nameof(player.PlanConstruction))] = ToLuaFunction<int, EBuilding>(player.PlanConstruction);

            table["faction"] = DynValue.FromObject(script, player.Faction);
            table["realm_index"] = DynValue.FromObject(script, player.RealmIndex);

            return DynValue.NewTable(table);
        }

        protected DynValue MakeRulesObject(GameRules rules)
        {
            // TODO
            return DynValue.Nil;

            //JsonSerializer.Serialize(rules);
            //string json = Newtonsoft.Json.JsonConvert.SerializeObject(rules);

            //DynValue val = DynValue.NewTable(JsonTableConverter.JsonToTable(json, script));

            //return val;
        }


        protected DynValue MakeAPI(ManagedRandom random)
        {
            Table table = new Table(script);

            table["int"] = (GetterDelegate)(() => DynValue.NewNumber(random.Next()));
            table["int_under"] = (OneParamGetterDelegate)((DynValue max) => DynValue.NewNumber(random.Next((int)max.Number)));

            return DynValue.NewTable(table);
        }

        protected void Log(DynValue value)
        {
            Log(value.ToDebugPrintString());
        }

        protected void Log(string val)
        {
            Console.WriteLine("LUA >>> " + val);
        }

        protected void Err(string str)
        {
            Console.WriteLine(str);
            throw new Exception(str);
        }

        protected virtual void InjectGlobals()
        {
            script.Globals["LOG"] = (OneParamNoReturnDelegate)Log;

            InjectEnum<EFactionFlag>();
            InjectEnum<EBuilding>();
            InjectEnum<EVotingCriteria>();
        }

        private void InjectEnum<T>() where T : struct, Enum
        {
            bool isString = !typeof(T).GetCustomAttributes(typeof(FlagsAttribute), true).Any();

            Script.GlobalOptions.CustomConverters.SetClrToScriptCustomConversion<T>(
                (script, v) => DynValue.NewString(v.ToString())
            );

            Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(
                isString ? DataType.String : DataType.Number,
                typeof(T),
                (dynVal) =>
                {
                    if (isString) {
                        bool findEnum(string str, out T parsed)
                        {
                            if (Enum.TryParse<T>(str, out parsed)) {
                                return true;
                            }

                            Err($"Unknown {typeof(T)} name {str}. Valid {typeof(T)} values are: {string.Join(", ", Enum.GetNames(typeof(T)))}");

                            return false;
                        }


                        if (findEnum(dynVal.String, out T castValue)) {
                            return castValue;
                        }
                    }
                    else {
                        if (Enum.IsDefined(typeof(T), (int)dynVal.Number)) {
                            return Enum.ToObject(typeof(T), (int)dynVal.Number);
                        }
                        else {
                            Err($"Unknown {typeof(T)} flag-set {(int)dynVal.Number}. Valid {typeof(T)} flags are: {string.Join(", ", Enum.GetValues(typeof(T)))}");
                        }
                    }

                    return default;
                }
            );


            var table = new Table(script);
            var values = Enum.GetValues(typeof(T));
            for (int i = 0; i < values.Length; i++) {
                T val = (T)values.GetValue(i);
                string str = val.ToString();
                table.Set(
                    PascalToSnake(str).ToUpper(),
                    isString ? DynValue.NewString(str) : DynValue.NewNumber((int)values.GetValue(i))
                );
            }

            script.Globals[typeof(T).Name.ToUpper()] = DynValue.NewTable(table);
        }

        private string PascalToSnake(string name)
        {
            StringBuilder snakeBuilder = new StringBuilder(char.ToLower(name[0]).ToString());

            for (int i = 1; i < name.Length; i++) {
                if (char.IsLower(name[i])) {
                    snakeBuilder.Append(name[i]);
                }
                else {
                    snakeBuilder.Append('_');
                    snakeBuilder.Append(char.ToLower(name[i]));
                }
            }

            return snakeBuilder.ToString();
        }

        private void CheckScriptValidity()
        {

            List<string> missingFunctions = new List<string>();
            foreach (var func in obligatoryFunctions) {
                if (script.Globals[func] == null || !(script.Globals[func] is Closure)) {
                    missingFunctions.Add(func);
                }
            }

            if (missingFunctions.Count > 0) {
                throw new MissingCriticalFunction(missingFunctions);
            }
        }

        public override string GetInternalName()
        {
            return name;
        }

        private void GeneratePersonas()
        {
            personas.Clear();

            if (script.Globals[GET_PERSONAS_FUNC] is Closure closure) {
                DynValue result = closure.Call();
                if (result.Type == DataType.Table) {
                    Table table = result.Table;

                    foreach (DynValue val in table.Values) {
                        if (val.Type == DataType.Table && val.Table is Table obj) {
                            ComputerPersona persona = new ComputerPersona();

                            persona.name = obj.Get("name").String;
                            persona.gender = (byte)obj.Get("gender").Number;

                            if (obj.Get("only_for_faction").IsNotNil()) {
                                persona.factionIndexFilter = (byte)obj.Get("only_for_faction").Number;
                            }

                            personas.Add(persona);
                        }
                    }
                }
            }

            if (personas.Count < MINIMUM_PERSONAS) {
                throw new Exception($"Not enough personas ({MINIMUM_PERSONAS} required, {personas.Count} found)");
            }
        }

        public override IReadOnlyList<ComputerPersona> GetPersonas()
        {
            return personas;
        }
    }
}
