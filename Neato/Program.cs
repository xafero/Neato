using System;
using System.ComponentModel;
using System.Configuration;
using System.Threading;
using System.Windows;
using CM = System.Configuration.ConfigurationManager;
using System.IO;
using System.Reflection;
using System.Linq;

namespace Neato
{
	public class Program
	{
		#region Core
		private static string assFile;
		private static int maxWaitAttempts;
		private static int waitIntervall;
		private static bool wait;
		private static IDelegator delegator;
		private static EventSink sink;
		
		private static void LoadConfig()
		{
			var appCfg = delegator.Settings;
			assFile = appCfg["file"];
			maxWaitAttempts = int.Parse(appCfg["maxWaitAttempts"]);
			waitIntervall = int.Parse(appCfg["waitIntervall"]);
			wait = bool.Parse(appCfg["wait"]);
		}
		
		[STAThread]
		public static void Main(string[] args)
		{
			// Check arguments
			if (args == null || args.Length != 1)
			{
				Console.Error.WriteLine("Invoke with one file!");
				Environment.ExitCode = -1;
				return;
			}
			// Get path
			var file = Path.GetFullPath(args.Single());
			var ext = Path.GetExtension(file);
			// Check file's existence
			if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
			{
				Console.Error.WriteLine("File doesn't exist! Please provide a valid one.");
				Environment.ExitCode = -2;
				return;
			}
			// Get delegator
			if (ext.ToLowerInvariant() != ".nto")
			{
				Console.Error.WriteLine("Only NTO files are supported!");
				Environment.ExitCode = -3;
				return;
			}
			delegator = new TextDelegator(file);
			LoadConfig();
			// Load executable
			var exe = Assembly.LoadFrom(assFile);
			var exePath = new Uri(exe.CodeBase).LocalPath;
			Console.WriteLine("Found executable => {0}", exePath);
			// Check for application config
			var configPath = exePath + ".config";
			if (File.Exists(configPath))
			{
				var map = new ExeConfigurationFileMap { ExeConfigFilename = configPath };
				var foreign = CM.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);
				Console.WriteLine("Found config => {0}", foreign.FilePath);
				// Overwrite own configuration with the foreign one
				var orig = CM.OpenExeConfiguration(ConfigurationUserLevel.None);
				foreign.SaveAs(orig.FilePath);
			}
			// Start side-thread
			ThreadPool.QueueUserWorkItem(WorkSideways);
			// Invoke main method
			exe.EntryPoint.Invoke(null, null);
			// Work is done
			Environment.ExitCode = 0;
			if (!wait)
				return;
			Console.Write("Press any key to continue . . . ");
			Console.ReadKey(true);
		}
		#endregion
		
		#region Wait for application handle
		private static void WorkSideways(object state)
		{
			Application app;
			while ((maxWaitAttempts--) > 0)
			{
				if ((app = Application.Current) != null)
				{
					WorkSideways(app);
					return;
				}
				Console.WriteLine("Waiting... {0}", maxWaitAttempts);
				Thread.Sleep(waitIntervall);
			}
			// Check again!
			if ((app = Application.Current) != null)
			{
				WorkSideways(app);
				return;
			}
			// No, nothing :-(
			Console.WriteLine("I'm tired of waiting!");
		}
		
		private static void WorkSideways(Application app)
		{
			sink = new EventSink(app, delegator);
			Console.WriteLine("Created new event sink for '{0}' with '{1}'...",
			                  app.GetType().FullName, delegator.GetType().FullName);
		}
		#endregion
	}
}