#if DEV_TOOLS
using System;
using System.IO;
using LuaInterface;
using UnityEngine;

namespace StarOverlay.Ingame;

/// <summary>
/// DEVELOPMENT BUILD ONLY. A Lua file that is run in the game without a restart: whenever <c>bpsr\state\lua-run.lua</c> (in the game folder) changes, it is run in
/// the game's own Lua state, and what it wrote with <c>log(...)</c> and any error stand in <c>bpsr\state\lua-out.txt</c>. <c>ser(value, depth)</c> turns a value
/// (also a table) into text. It is how the game's own functions are found and tried: package.loaded holds its modules, _G.Z its helpers.
/// </summary>
static class LuaDev
{
    static string _runStamp;
    static float _next;

    internal static void Tick()
    {
        try
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 1f;
            var dir = Path.Combine(Plugin.GameRoot, "bpsr", "state");
            var file = Path.Combine(dir, "lua-run.lua");
            if (!File.Exists(file)) return;
            var info = new FileInfo(file);
            var stamp = info.LastWriteTimeUtc.Ticks + ":" + info.Length;
            if (stamp == _runStamp) return;
            LuaState state;
            try
            {
                state = LuaClient.GetMainState();
            }
            catch (NullReferenceException)
            {
                return; // the game's Lua is not up yet: tried again
            }

            if (state == null) return;
            _runStamp = stamp;
            var outPath = Path.Combine(dir, "lua-out.txt");
            File.WriteAllText(outPath, $"run at {DateTime.Now:HH:mm:ss}\n");
            try
            {
                state.DoString(Prelude.Replace("@PATH@", outPath.Replace((char)92, '/')) + File.ReadAllText(file), "StarOverlayRun");
                File.AppendAllText(outPath, "done\n");
            }
            catch (Exception e)
            {
                File.AppendAllText(outPath, "error: " + e.Message + "\n");
            }
        }
        catch (Exception)
        {
        }
    }

    const string Prelude = @"
local PATH = ""@PATH@""
local function log(s)
  local f = io.open(PATH, 'a')
  if f then f:write(s .. '\n'); f:close() end
end
local function ser(v, d)
  d = d or 0
  local t = type(v)
  if t == 'table' then
    if d >= 4 then return '{...}' end
    local parts = {}
    local n = 0
    for k, x in pairs(v) do
      n = n + 1
      if n > 25 then parts[#parts + 1] = '...'; break end
      parts[#parts + 1] = tostring(k) .. '=' .. ser(x, d + 1)
    end
    return '{' .. table.concat(parts, ', ') .. '}'
  elseif t == 'string' then
    return '""' .. v .. '""'
  else
    return tostring(v)
  end
end
";
}
#endif
