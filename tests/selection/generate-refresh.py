from pathlib import Path
here=Path(__file__).resolve().parent
root=here.parents[1]
def block(text,marker):
 start=text.index(marker);pos=text.index('{',start);depth=1;end=pos+1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
def statement(text,marker):
 start=text.index(marker);return text[start:text.index(';',start)+1]
menu=(root/'src/MenuUi.cs').read_text()
dest=(root/'src/StartDestinations.cs').read_text()
src='''// Production scheduling and identity predicates extracted verbatim.
using System;using System.Linq;using System.Collections.Generic;
namespace MenuRefreshBoundary {
static class Time {internal static float realtimeSinceStartup;}
static class StartPorts {internal static int Revision;}
sealed class Plugin {internal static Plugin Instance=new Plugin();internal event Action SettingsReloaded;internal void ReloadConfigFile(){}internal void DebugLog(string s){}internal void Warn(string s){}internal void Error(string s,Exception e){}}
sealed class GameObject {internal bool activeInHierarchy=true;}
sealed class SelectionCatalog {internal List<int> Ports=new List<int>{1},Boats=new List<int>{1};internal List<string> Rejections=new List<string>();internal static int Calls;internal static bool Fail,Empty;internal static SelectionCatalog Discover(object o){Calls++;if(Fail)throw new Exception("fixture discovery failure");var c=new SelectionCatalog();if(Empty)c.Ports.Clear();return c;}}
sealed class MenuUi {
internal bool built=true;internal object settings=new object();internal Func<object> overrides;internal SelectionCatalog catalog;internal bool catalogFaultLogged,catalogUnavailable;internal GameObject gameObject=new GameObject();internal bool editing,EditingAny;internal object confirm,back;internal Plugin reloadSource;internal float nextDiscoveryRefresh;internal int registrationRevision=-1;internal int displays;
void HideNativeRegionChoice(){}void ApplyContinueState(){}void Hide(object o){}void RefreshEditor(){}void ShowCatalogUnavailable(string reason){catalog=null;catalogUnavailable=true;}void RefreshDisplay(){displays++;}void OnSettingsReloaded(){RefreshCatalog();}
internal void Open()=>OnEnable();internal void Tick()=>Update();
'''
src+='\n'.join(block(menu,m) for m in ['private void OnEnable()','private void Update()','internal void RefreshCatalog()'])
src+='''
}
class Component {internal bool enabled=true;internal Scene scene=new Scene();}
class Scene {internal bool isLoaded=true;internal bool IsValid()=>true;}
class Port:Component {internal static Port[] ports;internal int portIndex,region;}
static class Resources {internal static Port[] ScenePorts;internal static int Calls;internal static T[] FindObjectsOfTypeAll<T>() {Calls++;return ScenePorts.Cast<T>().ToArray();}}
static class StartDestinations {internal static bool Loaded(Component c)=>c!=null&&c.scene.isLoaded;
'''
src+='\n'.join(statement(dest,m) for m in ['internal static bool ValidPort(Port port) =>','internal static bool ValidPort(Port port, Port[] scenePorts) =>'])
src+='\n}\n}\n'
(here/'MenuRefresh.generated.cs').write_text(src)
