using System;
using System.Reflection;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Opens the game's own item card (the one a click on an item link in the game's chat shows) by running its Lua call
/// <c>Z.TipsVM.OpenItemTipsView</c> in the game's main Lua state.
/// </summary>
static class GameItemTips
{
    static object _state;
    static MethodInfo _doString;
    static bool _unavailable;
    static int _logged;

    /// <summary>True when the call was made (the card is the game's job from then on); false when it could not be (logged; nothing of ours is drawn instead).</summary>
    internal static bool Show(int itemId, Vector2 screenPoint)
    {
        if (_unavailable || itemId <= 0) return false;
        try
        {
            if (!Resolve()) return false;
            var x = screenPoint.x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var y = screenPoint.y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            // the fields are the ones the game's own views fill: item, where it pops up, and whether it has a dimmed background
            var chunk = "Z.TipsVM.CloseItemTipsView() "
                        + "Z.TipsVM.OpenItemTipsView({ configId = " + itemId + ", itemUuid = 0, showType = E.EItemTipsShowType.Default, "
                        + "posType = E.EItemTipsPopType.ScreenPosition, screenPosition = Vector2(" + x + ", " + y + "), isShowBg = true, isResident = false })";
            _doString.Invoke(_state, new object[] { chunk, "StarOverlay.ItemTips" });
            if (_logged++ < 3) Plugin.L.LogInfo("game item card opened for " + itemId);
            return true;
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: not null } t ? t.InnerException : e;
            if (_logged++ < 6) Plugin.L.LogWarning("game item card: " + inner.Message);
            return false;
        }
    }

    static bool Resolve()
    {
        if (_doString != null && _state != null) return true;
        Type client = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name != "ZLuaFramework") continue;
            client = assembly.GetType("LuaClient");
            if (client != null) break;
        }

        if (client == null)
        {
            _unavailable = true;
            Plugin.L.LogWarning("game item card: the game's Lua client was not found");
            return false;
        }

        _state = client.GetMethod("GetMainState", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        if (_state == null) return false; // not up yet: asked again at the next click
        // two methods are called DoString (one generic that returns a value): the plain one is the one wanted
        foreach (var method in _state.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != "DoString" || method.IsGenericMethodDefinition) continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string)) _doString = method;
        }

        if (_doString == null)
        {
            _unavailable = true;
            Plugin.L.LogWarning("game item card: DoString was not found");
            return false;
        }

        return true;
    }
}
