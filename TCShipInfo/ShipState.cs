using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using KSP.UI.Screens;

namespace ShipState
{
	[KSPAddon(KSPAddon.Startup.TrackingStation, false)]
	public class ShowShipStatePlugin : MonoBehaviour
	{
		public List<IPartVisitor> visitors = new List<IPartVisitor>();
		string shipInfo;
		string shipName;
		bool show;
		Rect windowRect = new Rect();

		public ShowShipStatePlugin() {
			visitors.Add(new CommandStatus());
		}

		public void ToggleView() {
			show = !show;
		}

		private void activeShipChanged(MapObject target) {
			if (target == null)
				return;

			updateVesselInfo(target.vessel);

			windowRect.width = 0;
			windowRect.height = 0;
		}

		private void updateVesselInfo(Vessel vessel) {
			if (vessel == null ||
				vessel.DiscoveryInfo.Level != DiscoveryLevels.Owned) {
				this.shipInfo = null;
				this.shipName = null;
				return;
			}

			ProtoVessel proto = vessel.protoVessel;
			double mass = 0;
			visitors.ForEach(v => v.reset());

			var res = new SortedDictionary<string, ResourceData>();
			foreach (ProtoPartSnapshot p in proto.protoPartSnapshots) {
				foreach (var r in p.resources) {
					ResourceData d;
					if (res.ContainsKey(r.resourceName))
						d = res[r.resourceName];
					else {
						d = new ResourceData(r.resourceName);
					}
//					var v = r.resourceValues;
//					d.current += doubleValue(v, "amount");
//					d.max += doubleValue(v, "maxAmount");
					d.current += r.amount;
					d.max += r.maxAmount;
					res[r.resourceName] = d;
				}

				visitors.ForEach(v => v.visit(p));
				mass += p.mass;
			}

			var texts = res.Values.ToList().ConvertAll(d => d.ToString());

			if (!vessel.isEVA) {
				texts.Add("");
				var crew = proto.GetVesselCrew().Count();
				mass += res.Values.Sum(d => d.GetMass());
				var parts = proto.protoPartSnapshots.Count();
				texts.Add(string.Format("Crew: {0}, Parts: {1}, Mass: {2:f2}t", crew, parts, mass));

				visitors.ForEach(v => texts.AddRange(v.getTexts()));
			}

			this.shipInfo = string.Join("\n", texts.ToArray());
			this.shipName = vessel.GetName();
		}

		GUISkin guiSkin;
		private void drawGuiLayout(int windowID) {
			GUILayout.BeginVertical();

			GUILayout.Label(this.shipInfo);

			GUILayout.EndVertical();
			GUI.DragWindow();
		}
		private void OnDrawGuiEvent() {
			if (show && this.shipInfo != null) {
				GUI.skin = guiSkin;
				windowRect = GUILayout.Window(1, windowRect, drawGuiLayout,
					this.shipName);
			}
		}

		private void OnGUI() {
			OnDrawGuiEvent();
		}

		void Start() {
			GameEvents.onPlanetariumTargetChanged.Add(activeShipChanged);
			GameEvents.onGUIApplicationLauncherReady.Add(AddToolbarButton);
			guiSkin = (GUISkin)UnityEngine.Object.Instantiate(HighLogic.Skin);
			guiSkin.label.wordWrap = false;

			var config = KSP.IO.PluginConfiguration.CreateForType<ShowShipStatePlugin>();
			config.load();
			windowRect.x = config.GetValue("window_x", 240);
			windowRect.y = config.GetValue("window_y", 35);
			show = config.GetValue("show", true);
		}

		ApplicationLauncherButton toolbarButton = null;

		private void AddToolbarButton() {
			GameEvents.onGUIApplicationLauncherReady.Remove(AddToolbarButton);
			toolbarButton = ApplicationLauncher.Instance.AddModApplication(
				ToggleView, ToggleView, null, null, null, null,
				ApplicationLauncher.AppScenes.TRACKSTATION,
				GameDatabase.Instance.GetTexture("TCShipInfo/shipinfo", false));
		}

		void OnDestroy() {
			var config = KSP.IO.PluginConfiguration.CreateForType<ShowShipStatePlugin>();
			config.SetValue("window_x", (int)windowRect.x);
			config.SetValue("window_y", (int)windowRect.y);
			config.SetValue("show", show);
			config.save();
			GameEvents.onPlanetariumTargetChanged.Remove(activeShipChanged);
			if (toolbarButton != null) {
				ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
				toolbarButton = null;
			}
		}

		private static double doubleValue(ConfigNode node, string key) {
			double v = 0d;
			Double.TryParse(node.GetValue(key), out v);
			return v;
		}
	}

	class ResourceData {
		public double current, max;

		public readonly string name;
		readonly PartResourceDefinition def;

		public ResourceData(string name) {
			this.name = name;
			this.def = PartResourceLibrary.Instance.GetDefinition(name);
		}

		public double GetMass() {
			return def == null ? 0 : def.density * current;
		}

		public override string ToString() {
			return string.Format("{0}: {1} / {2}", name, s(current), s(max));
		}

		private static string s(double d) {
			return d.ToString(d < 100 ? "f2" : "f0");
		}
	}

	public interface IPartVisitor {
		void reset();
		void visit(ProtoPartSnapshot part);
		IEnumerable<string> getTexts();
	}

	class CommandStatus : IPartVisitor {
		public enum Statuses { none, seat, pod }
		public Statuses status = Statuses.none;
		public void reset() {
			status = Statuses.none;
		}

		public void visit(ProtoPartSnapshot part) {
			if (status == Statuses.pod) return;

			foreach (var m in part.modules) {
				if (m.moduleName == "ModuleCommand") {
					status = Statuses.pod;
					return;
				}
				if (m.moduleName == "KerbalSeat") {
					status = Statuses.seat;
				}
			}
		}
		public IEnumerable<string> getTexts() {
			switch (this.status) {
			case Statuses.pod:
				return new String[0];
			case Statuses.none:
				return new[] {"No command pod"};
			case Statuses.seat:
				return new[] {"Has command seat"};
			}
			throw new Exception("unknown status " + status);
		}
	}
}
