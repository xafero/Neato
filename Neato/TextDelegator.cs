using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Neato
{
	public class TextDelegator : IDelegator
	{
		private static readonly string configPrefix = "config.";
		private static readonly string exitTag = "[---]";
		
		private readonly string[] lines;
		private readonly ISet<object> sources;
		private readonly ISet<object> objectDump;

		public TextDelegator(string file)
		{
			sources = new HashSet<object>();
			objectDump = new HashSet<object>();
			lines = File.ReadLines(file).ToArray();
			Settings = lines.TakeWhile(l => l.StartsWith(configPrefix, StringComparison.Ordinal))
				.Select(l => l.Split(new [] { '=' }, 2)).ToDictionary(
					k => k[0].Replace(configPrefix,"").Trim(), v => v[1].Trim());
		}
		
		public IDictionary<string, string> Settings { get; private set; }

		public void Process<TSender, TEvent, TArgs>(TSender s, TEvent e, TArgs a) where TSender : class
		{
			// Ensure accessibility
			if (s != null)
				sources.Add(s);
			// Build path from event
			var path = string.Format("{0}~{1}", s, e);
			Console.WriteLine(" < " + path + " > ");
			// Find statements for this event
			var entryTag = "["+path+"]";
			var script = lines.SkipWhile(l => l != entryTag).TakeWhile(l => l != exitTag).ToArray();
			if (script.Length < 1)
				return;
			// Get all elements necessary
			var apps = sources.OfType<Application>();
			var windows = sources.OfType<Window>().Concat(
				sources.OfType<Window>().SelectMany(w => w.OwnedWindows.OfType<Window>()));
			var panels = windows.Select(w => w.Content).OfType<Panel>();
			var elements = panels.SelectMany(FindChildren).Distinct();
			var menu = elements.OfType<Menu>();
			var menus = menu.SelectMany(m => m.Items.OfType<MenuItem>());
			// Execute it!
			ThreadPool.QueueUserWorkItem(o => Execute(s, e, a, script, menus, elements, windows));
		}
		
		private void Execute<TSender, TEvent, TArgs>(TSender s, TEvent e, TArgs a, string[] script,
		                                             IEnumerable<MenuItem> menus, IEnumerable<UIElement> elements,
		                                             IEnumerable<Window> windows)
			where TSender : class
		{
			foreach (var line in script.Skip(1).Select(l => l.Trim()))
			{
				if (line.Length < 1)
					continue;
				var parts = line.Split(new [] { ' ' }, 2);
				var cmd = parts.First().ToLowerInvariant();
				var dispatch = s == null ? null : (s as DispatcherObject).Dispatcher;
				switch (cmd)
				{
					case "menuclick":
						dispatch.BeginInvoke((Action)(() => ExecuteMenuClick(parts, menus)));
						break;
					case "mousetop":
						var inputElem = Mouse.DirectlyOver;
						var text = inputElem+"";
						var depObj = inputElem as DependencyObject;
						if (depObj != null)
						{
							var parents = FindParents(depObj);
							text += " (" + string.Join("\n ->", parents.Zip(
								Enumerable.Range(1,parents.Count), (x,y) => y.ToString("D2")+": "+x)) +")";
						}
						Console.WriteLine("Top element is '{0}'.", text);
						break;
					case "delay":
						var arg = int.Parse(parts.Skip(1).First());
						Console.WriteLine("Sleeping for {0} ms...", arg);
						Thread.Sleep(arg);
						break;
					case "execcmd":
						dispatch.BeginInvoke((Action)(() => ExecuteCommand(parts, elements)));
						break;
					case "pull":
						dispatch.BeginInvoke((Action)(() => ExecutePull(parts, windows)));
						break;
					case "push":
						dispatch.BeginInvoke((Action)(() => ExecutePush(parts)));
						break;
					case "purge":
						dispatch.BeginInvoke((Action)(() => ExecutePurge(parts)));
						break;
					default:
						throw new NotImplementedException(cmd+"!");
				}
			}
		}
		
		private void ExecutePurge(string[] parts)
		{
			var args = parts.Skip(1).First();
			var myObjs = objectDump.Where(o => o.GetType().Name == args).ToArray();
			Array.ForEach(myObjs, i => objectDump.Remove(i));
			var newest = myObjs.LastOrDefault();
			if (newest != null)
				objectDump.Add(newest);
			var count = Math.Max(0, myObjs.Length-1);
			Console.WriteLine("Purged {0} items.", count);
		}
		
		private void ExecutePush(string[] parts)
		{
			var args = parts.Skip(1).First().Split(new [] {'='}, 2);
			var nameParts = args[0].Trim().Split(new [] {'/'}, 2);
			var value = args[1].Trim();
			var myObj = objectDump.FirstOrDefault(o => o.GetType().Name == nameParts[0]);
			if (myObj == null)
			{
				Console.Error.WriteLine("Object '{0}' doesn't exist!", args[0]);
				return;
			}
			object myVal = value;
			if (value.ToCharArray().First() == '#')
				myVal = objectDump.FirstOrDefault(o => o.GetType().Name == value.Substring(1));
			myObj.GetType().GetProperty(nameParts[1]).SetValue(myObj, myVal);
			Console.WriteLine("Pushed '{0}' as '{1}' into '{2}'.", myVal, nameParts[1], myObj);
		}
		
		private void ExecutePull(string[] parts, IEnumerable<Window> windows)
		{
			// Refresh that!
			var tmp = windows.ToArray();
			Console.WriteLine("Refreshed {0} windows.", tmp.Length);
			var args = parts.Skip(1).First().Split('/');
			var myObj = windows.Concat(objectDump).FirstOrDefault(o => o.GetType().Name == args[0]);
			if (myObj == null)
			{
				Console.Error.WriteLine("Object '{0}' doesn't exist!", args[0]);
				return;
			}
			var propParts = args[1].Split(new [] {'|'}, 2);
			var propName = propParts[0];
			var myVal = myObj.GetType().GetProperty(propName).GetValue(myObj);
			if (propParts.Length >= 2)
			{
				var propIdx = int.Parse(propParts[1]);
				var items = ((IEnumerable)myVal).OfType<object>();
				myVal = items.Skip(propIdx).Take(1).Single();
			}
			objectDump.Add(myVal);
			Console.WriteLine("Pulled '{0}' from '{1}'.", myVal, myObj);
		}

		private void ExecuteCommand(string[] parts, IEnumerable<UIElement> elements)
		{
			// Refresh that!
			var tmp = elements.ToArray();
			Console.WriteLine("Refreshed {0} items.", tmp.Length);
			var args = parts.Skip(1).First().Split('/');
			var myObj = objectDump.FirstOrDefault(o => o.GetType().Name == args[0]);
			if (myObj == null)
			{
				Console.Error.WriteLine("Object '{0}' doesn't exist!", args[0]);
				return;
			}
			var paramParts = args[1].Split(' ');
			var myCmd = myObj.GetType().GetProperty(paramParts[0]).GetValue(myObj);
			var cmdMeth = myCmd.GetType().GetMethod("Execute");
			object param = null;
			if (paramParts.Length >= 2)
				param = objectDump.FirstOrDefault(o => o.GetType().Name == paramParts[1]);
			cmdMeth.Invoke(myCmd, new object[] { param });
			Console.WriteLine("Executed '{0}' from '{1}'.", myCmd, myObj);
		}
		
		private void ExecuteMenuClick(string[] parts, IEnumerable<MenuItem> menus)
		{
			var args = parts.Skip(1).First().Split('/');
			var myMenu = menus.FirstOrDefault(m => (m.Header+"") == args[0]);
			var myItem = myMenu.Items.OfType<MenuItem>().FirstOrDefault(i => (i.Header+"") == args[1]);
			var im = myItem.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
			im.Invoke(myItem, null);
			Console.WriteLine("Clicked on menu '{0}' and '{1}'.", myMenu.Header, myItem.Header);
		}
		
		#region WPF stuff
		private Stack<DependencyObject> FindParents(DependencyObject obj)
		{
			var results = new Stack<DependencyObject>();
			var parent = obj;
			while ((parent = VisualTreeHelper.GetParent(parent)) != null)
				results.Push(parent);
			return results;
		}
		
		private IEnumerable<UIElement> FindChildren(Panel panel)
		{
			return FindChildren(panel.Children.OfType<UIElement>());
		}
		
		private IEnumerable<UIElement> FindChildren(Decorator decorator)
		{
			return FindChildren(Enumerable.Repeat(decorator.Child, 1));
		}
		
		private IEnumerable<UIElement> FindChildren(ContentControl control)
		{
			var cc = control.Content as UIElement;
			if (cc == null)
			{
				if (control.Content != null)
					objectDump.Add(control.Content);
				return Enumerable.Empty<UIElement>();
			}
			return FindChildren(Enumerable.Repeat(cc, 1));
		}
		
		private IEnumerable<UIElement> FindChildren(ToolBarTray toolbar)
		{
			return FindChildren(toolbar.ToolBars.SelectMany(t => t.Items.OfType<UIElement>()));
		}
		
		private IEnumerable<UIElement> FindChildren(StatusBar bar)
		{
			return FindChildren(bar.Items.OfType<UIElement>());
		}
		
		private IEnumerable<UIElement> FindChildren(ItemsControl control)
		{
			return FindChildren(control.Items.OfType<UIElement>());
		}
		
		private IEnumerable<UIElement> FindChildren(ContentPresenter control)
		{
			var cc = control.Content as UIElement;
			return cc == null ? Enumerable.Empty<UIElement>() : FindChildren(Enumerable.Repeat(cc, 1));
		}
		
		private IEnumerable<UIElement> FindChildren(IEnumerable<UIElement> elements)
		{
			var results = new List<UIElement>();
			foreach (var item in elements)
			{
				// Special types
				if (item.GetType().Name == "DockSite")
				{
					var type = item.GetType();
					var docs = ((IEnumerable)type.GetProperty("Documents").GetValue(item))
						.OfType<UIElement>();
					results.AddRange(FindChildren(docs));
					var wins = ((IEnumerable)type.GetProperty("DocumentWindows").GetValue(item))
						.OfType<UIElement>();
					results.AddRange(FindChildren(wins));
					var tools = ((IEnumerable)type.GetProperty("ToolWindows").GetValue(item))
						.OfType<UIElement>();
					results.AddRange(FindChildren(tools));
					var host = (ContentControl)type.GetProperty("MdiHost").GetValue(item);
					if (host != null)
						results.AddRange(FindChildren(host));
					var cont = (UIElement)type.GetProperty("Content").GetValue(item);
					if (host != null)
						results.AddRange(FindChildren(host));
				}
				if (item.GetType().Name == "SplitContainer")
				{
					var type = item.GetType();
					var children = ((IEnumerable)type.GetProperty("Children").GetValue(item))
						.OfType<UIElement>();
					results.AddRange(FindChildren(children));
				}
				// WPF types
				if (item is Decorator)
					results.AddRange(FindChildren((Decorator)item));
				if (item is Panel)
					results.AddRange(FindChildren((Panel)item));
				if (item is ContentControl)
					results.AddRange(FindChildren((ContentControl)item));
				if (item is ToolBarTray)
					results.AddRange(FindChildren((ToolBarTray)item));
				if (item is StatusBar)
					results.AddRange(FindChildren((StatusBar)item));
				if (item is ItemsControl)
					results.AddRange(FindChildren((ItemsControl)item));
				if (item is ContentPresenter)
					results.AddRange(FindChildren((ContentPresenter)item));
				// Something different
				results.Add(item);
			}
			return results;
		}
		#endregion
	}
}