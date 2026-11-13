// Test driver in the disposable game's Managed folder, never part of either published mod.
// Exercises real visitor-mod code with an in-memory visit and never sends a public trade.
using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json;

public static class IsolatedVisitorTest {
    static Type runtime, visit, transfer, sessionType, tradeType;
    static object session, wire, trade, hostWire;
    static Type TypeOf(string name) {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "ProbablyMultiplayer" && assembly.GetName().Version.ToString(3) == "2.6.1")
                return assembly.GetType("ProbablyMultiplayer." + name, false);
        return null;
    }
    static MethodInfo Method(Type type, string name) { return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
    static FieldInfo Field(Type type, string name) { return type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
    public static void DismissLogin() {
        var type = TypeOf("PlayerNameUI"); var method = type == null ? null : Method(type, "Dismiss");
        if (method != null) method.Invoke(null, null);
    }
    public static bool LoginVisible() {
        var type = TypeOf("PlayerNameUI"); var property = type == null ? null : type.GetProperty("Visible", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return property != null && (bool)property.GetValue(null, null);
    }
    public static bool Loaded() {
        foreach (var mod in ModLoader.Mods)
            if (mod != null && mod.IsEnabled && mod.Manifest != null && mod.Manifest.ID == "lemocuber_probablymultiplayer" && mod.Manifest.ModVersion == "2.6.1") return true;
        return false;
    }
    public static void SeedStaleSession(){var rt=TypeOf("MultiplayerRuntime");var st=TypeOf("VisitSession");Field(rt,"Session").SetValue(null,Activator.CreateInstance(st,true));}
    public static bool HasSession(){return Field(TypeOf("MultiplayerRuntime"),"Session").GetValue(null)!=null;}
    public static GameItem Begin() {
        runtime = TypeOf("MultiplayerRuntime"); visit = TypeOf("NativeVisit"); transfer = TypeOf("ItemTransfer"); sessionType = TypeOf("VisitSession"); tradeType = TypeOf("Trade");
        if (runtime == null || visit == null || transfer == null || sessionType == null || tradeType == null) throw new InvalidOperationException("Visitor 2.6.1 interaction methods are missing");
        const string hostId = "isolated-host", visitorId = "isolated-visitor", visitId = "isolated-visit";
        var stock = DirectoryMaster.Item("magnifier", true); stock.name = "PM_REAL_VISITOR_ITEM"; GeneralHelper.SetItemOwned(stock, true); stock.GetUniqueID();
        wire = Method(transfer, "Capture").Invoke(null, new object[] { stock, null }); stock.Destroy();
        if (wire == null) throw new InvalidOperationException("The visitor mod could not serialize the test item");
        string itemJson = JsonConvert.SerializeObject(wire);
        string sessionJson = "{\"id\":\"" + visitId + "\",\"friend\":true,\"host\":{\"userId\":\"" + hostId + "\",\"nickname\":\"Host\"},\"visitor\":{\"userId\":\"" + visitorId + "\",\"nickname\":\"Visitor\",\"spriteUrl\":\"\"},\"offers\":{\"" + hostId + "\":[],\"" + visitorId + "\":[" + itemJson + "]},\"rejected\":{},\"buyerId\":\"" + hostId + "\",\"selected\":[]}";
        session = JsonConvert.DeserializeObject(sessionJson, sessionType);
        Field(runtime, "PlayerId").SetValue(null, hostId); Field(runtime, "Session").SetValue(null, session);
        Method(visit, "Begin").Invoke(null, new[] { session, (object)"Visitor" });
        Method(visit, "Apply").Invoke(null, new[] { session });
        string tradeJson = "{\"id\":\"isolated-trade\",\"visitId\":\"" + visitId + "\",\"sellerId\":\"" + visitorId + "\",\"buyerId\":\"" + hostId + "\",\"price\":125,\"items\":[" + itemJson + "]}";
        trade = JsonConvert.DeserializeObject(tradeJson, tradeType);
        var isPreview = Method(visit, "IsPreview");
        foreach (var item in EmporiumEntry.Instance.frontInvinvElement.childItems)
            if (item != null && item.name == "PM_REAL_VISITOR_ITEM" && (bool)isPreview.Invoke(null, new object[] { item })) return item;
        throw new InvalidOperationException("The real visitor mod did not create the offered preview");
    }
    public static bool PrepareRejectedForHold() {
        try { Method(transfer, "Prepare").Invoke(null, new[] { trade }); return false; }
        catch (TargetInvocationException error) {
            if (error.InnerException is InvalidOperationException && error.InnerException.Message.StartsWith("Another player is moving", StringComparison.Ordinal)) return true;
            throw;
        }
    }
    public static void Arrive() { Field(visit, "Arrived").SetValue(null, true); }
    public static void IncomingChat(string text) {
        var nativeChat=TypeOf("NativeChat");Method(nativeChat,"Receive").Invoke(null,new object[]{"isolated-visitor","Visitor",text,null,false});
    }
    public static void HostOffer(GameItem item) {
        item.GetUniqueID();hostWire=Method(transfer,"Capture").Invoke(null,new object[]{item,null});
        var offers=(IDictionary)sessionType.GetProperty("Offers").GetValue(session,null);((IList)offers["isolated-host"]).Add(hostWire);
    }
    public static void TradeScenario(bool hostIsBuyer,int revision) {
        sessionType.GetProperty("BuyerId").SetValue(session,hostIsBuyer?"isolated-host":"isolated-visitor",null);
        sessionType.GetProperty("Revision").SetValue(session,revision,null);
        var selected=(IList)sessionType.GetProperty("Selected").GetValue(session,null);selected.Clear();
        var selectedWire=hostIsBuyer?wire:hostWire;selected.Add((string)selectedWire.GetType().GetProperty("Id").GetValue(selectedWire,null));
        object quote=null;if(hostIsBuyer){var type=TypeOf("Quote");quote=Activator.CreateInstance(type,true);type.GetProperty("SellerId").SetValue(quote,"isolated-visitor",null);type.GetProperty("Price").SetValue(quote,125,null);type.GetProperty("Revision").SetValue(quote,revision,null);}
        sessionType.GetProperty("Quote").SetValue(session,quote,null);
    }
    public static void Prepare() { Method(transfer, "Prepare").Invoke(null, new[] { trade }); }
    public static void Commit() {
        try { Method(transfer, "Commit").Invoke(null, new[] { trade }); }
        finally { Method(transfer, "Release").Invoke(null, null); }
    }
    public static void End() {
        Method(visit, "End").Invoke(null, new object[] { true, false });
        Field(runtime, "Session").SetValue(null, null);
    }
}
