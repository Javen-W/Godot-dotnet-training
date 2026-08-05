using Godot;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GodotDotnetTraining
{
	/// <summary>
	/// Centralized logging system for debugging and error tracking.
	/// Formats every entry with timestamp, level, compact thread id, and an automatic
	/// <c>[ClassName] MethodName():</c> caller prefix so call sites only supply the message body.
	/// </summary>
	public partial class Logger : Node
	{
		#region Constants

		private const string LOGS_DIRECTORY = "user://logs/sessions/";
		private const int LOG_RETENTION_COUNT = 10;
		private const int FILE_FLUSH_BATCH_SIZE = 32;
		private static readonly TimeSpan FILE_FLUSH_INTERVAL = TimeSpan.FromMilliseconds(250);
		private static readonly Regex AsyncStateMachineMethodNameRegex = new(
			@"^<(.+)>d__\d+$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		#endregion

		#region Nested Types

		private readonly record struct LogEntry(LogLevel Level, string Message);

		private readonly record struct CallerInfo(string ClassName, string MemberName);

		#endregion

		#region Static State

		public static Logger Instance { get; private set; }
		public static string CurrentLogFilePath => Instance?._currentLogFilePath ?? string.Empty;
		public static string CurrentLogVirtualPath => Instance?._currentLogVirtualPath ?? string.Empty;

		#endregion

		#region Instance State

		private string _currentLogFilePath;
		private string _currentLogVirtualPath;
		private BlockingCollection<LogEntry> _logQueue;
		private CancellationTokenSource _logWorkerCancellation;
		private Task _logWorkerTask;
		private bool _initialized;

		#endregion

		#region Godot Lifecycle

		/// <summary>
		/// Registers the singleton instance and starts the background log writer for this session.
		/// </summary>
		public override void _Ready()
		{
			Instance = this;
			InitializeLogger();
		}

		/// <summary>
		/// Flushes queued log entries and clears the singleton when the logger leaves the tree.
		/// </summary>
		public override void _ExitTree()
		{
			ShutdownLogger();
			if (Instance == this)
			{
				Instance = null;
			}
		}

		#endregion

		#region Public API

		/// <summary>
		/// Writes a DEBUG entry. DEBUG is file-only and includes the automatic caller prefix.
		/// </summary>
		/// <param name="args">Message fragments joined into the log body.</param>
		public static void Debug(params object[] args)
		{
			Instance?.Log(LogLevel.DEBUG, FormatMessage(args));
		}

		/// <summary>
		/// Writes an INFO entry to file and console with the automatic caller prefix.
		/// </summary>
		/// <param name="args">Message fragments joined into the log body.</param>
		public static void Info(params object[] args)
		{
			Instance?.Log(LogLevel.INFO, FormatMessage(args));
		}

		/// <summary>
		/// Writes a WARNING entry to file and console with the automatic caller prefix.
		/// </summary>
		/// <param name="args">Message fragments joined into the log body.</param>
		public static void Warning(params object[] args)
		{
			Instance?.Log(LogLevel.WARNING, FormatMessage(args));
		}

		/// <summary>
		/// Writes an ERROR entry to file and console with the automatic caller prefix.
		/// </summary>
		/// <param name="args">Message fragments joined into the log body.</param>
		public static void Error(params object[] args)
		{
			Instance?.Log(LogLevel.ERROR, FormatMessage(args));
		}

		/// <summary>
		/// Formats and enqueues one log line using the standard project log shape.
		/// </summary>
		/// <param name="level">Severity of the entry.</param>
		/// <param name="message">Caller-supplied message body without class/method prefix.</param>
		public void Log(LogLevel level, string message)
		{
			if (!_initialized)
			{
				return;
			}

			var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			var threadId = Thread.CurrentThread.ManagedThreadId;
			var caller = ResolveCaller();
			var formattedMessage =
				$"[{timestamp}] [{level}] [T{threadId}] [{caller.ClassName}] {caller.MemberName}(): {message}";
			EnqueueLog(new LogEntry(level, formattedMessage));
		}

		#endregion

		#region Initialization / Shutdown

		/// <summary>
		/// Creates the session log file and starts the background queue consumer.
		/// </summary>
		private void InitializeLogger()
		{
			if (_initialized)
			{
				return;
			}

			try
			{
				var logsDirectory = ResolveLogsDirectory();
				var absoluteLogsDir = ProjectSettings.GlobalizePath(logsDirectory);
				Directory.CreateDirectory(absoluteLogsDir);

				var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss");
				_currentLogVirtualPath = $"{logsDirectory}session_{timestamp}.log";
				_currentLogFilePath = Path.Combine(absoluteLogsDir, $"session_{timestamp}.log");

				// Defer cleanup so SettingsContainer has time to initialize first,
				// since Logger is registered before SettingsContainer in the autoload order.
				Callable.From(CleanupOldLogFiles).CallDeferred();

				_logQueue = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>());
				_logWorkerCancellation = new CancellationTokenSource();
				_logWorkerTask = Task.Run(ProcessLogQueueAsync);
				_initialized = true;

				EnqueueLog(new LogEntry(LogLevel.INFO, $"=== Session started at {DateTimeOffset.Now:O} ==="));
				EnqueueLog(new LogEntry(LogLevel.INFO, $"OS: {OS.GetName()}"));
				EnqueueLog(new LogEntry(LogLevel.INFO, $"Engine: {Engine.GetVersionInfo()["string"]}"));
				EnqueueLog(new LogEntry(LogLevel.INFO, string.Empty));
			}
			catch (Exception ex)
			{
				GD.PushError($"Logger: Failed to initialize: {ex.Message}");
			}
		}

		/// <summary>
		/// Stops the background writer and disposes queue resources during teardown.
		/// </summary>
		private void ShutdownLogger()
		{
			if (!_initialized)
			{
				return;
			}

			try
			{
				_logQueue?.CompleteAdding();
				if (_logWorkerTask != null && !_logWorkerTask.Wait(TimeSpan.FromSeconds(2)))
				{
					_logWorkerCancellation?.Cancel();
					_logWorkerTask.Wait(TimeSpan.FromSeconds(2));
				}
			}
			catch (Exception ex)
			{
				GD.PushError($"Logger: Failed to flush shutdown logs: {ex.Message}");
			}
			finally
			{
				_logWorkerCancellation?.Dispose();
				_logWorkerCancellation = null;
				_logWorkerTask = null;
				_logQueue?.Dispose();
				_logQueue = null;
				_initialized = false;
			}
		}

		#endregion

		#region Formatting

		/// <summary>
		/// Joins optional message fragments into a single log body string.
		/// </summary>
		/// <param name="args">Fragments to join with spaces.</param>
		/// <returns>The joined message body, or an empty string when no fragments are supplied.</returns>
		private static string FormatMessage(params object[] args)
		{
			if (args == null || args.Length == 0)
			{
				return string.Empty;
			}

			if (args.Length == 1)
			{
				return args[0]?.ToString() ?? string.Empty;
			}

			return string.Join(" ", args.Select(arg => arg?.ToString() ?? string.Empty));
		}

		/// <summary>
		/// Walks the stack to find the first non-Logger caller and returns its class/method names
		/// for the automatic log prefix, including a best-effort decode of async state machines.
		/// </summary>
		/// <returns>Resolved caller class and member names, or Unknown placeholders.</returns>
		private static CallerInfo ResolveCaller()
		{
			var stackTrace = new StackTrace(1, fNeedFileInfo: false);
			var frames = stackTrace.GetFrames();
			if (frames == null || frames.Length == 0)
			{
				return new CallerInfo("Unknown", "Unknown");
			}

			foreach (var frame in frames)
			{
				var method = frame?.GetMethod();
				if (method == null)
				{
					continue;
				}

				var declaringType = method.DeclaringType;
				if (declaringType == null || declaringType == typeof(Logger))
				{
					continue;
				}

				// Prefer the enclosing user type when the frame is an async/iterator state machine.
				if (IsCompilerGeneratedType(declaringType))
				{
					var ownerType = declaringType.DeclaringType ?? declaringType;
					var asyncMethodName = TryGetAsyncMethodName(declaringType.Name) ?? method.Name;
					return new CallerInfo(ownerType.Name, asyncMethodName);
				}

				return new CallerInfo(declaringType.Name, method.Name);
			}

			return new CallerInfo("Unknown", "Unknown");
		}

		/// <summary>
		/// Detects compiler-generated types such as async state machines.
		/// </summary>
		/// <param name="type">Type under inspection.</param>
		/// <returns>True when the type is compiler-generated.</returns>
		private static bool IsCompilerGeneratedType(Type type)
		{
			return type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
				|| type.Name.Contains('<')
				|| type.Name.Contains('>');
		}

		/// <summary>
		/// Extracts the original method name from an async state-machine type name when possible.
		/// </summary>
		/// <param name="generatedTypeName">Compiler-generated type name.</param>
		/// <returns>Original method name, or null when the pattern does not match.</returns>
		private static string TryGetAsyncMethodName(string generatedTypeName)
		{
			var match = AsyncStateMachineMethodNameRegex.Match(generatedTypeName);
			return match.Success ? match.Groups[1].Value : null;
		}

		#endregion

		#region Queue / IO

		/// <summary>
		/// Enqueues a fully formatted log line for the background writer.
		/// </summary>
		/// <param name="entry">Formatted log entry.</param>
		private void EnqueueLog(LogEntry entry)
		{
			if (_logQueue == null || _logQueue.IsAddingCompleted)
			{
				return;
			}

			try
			{
				_logQueue.Add(entry);
			}
			catch (Exception ex)
			{
				GD.PushError($"Logger: Failed to enqueue log entry: {ex.Message}");
			}
		}

		/// <summary>
		/// Consumes the log queue on a background thread, mirroring non-DEBUG levels to the console.
		/// </summary>
		private async Task ProcessLogQueueAsync()
		{
			try
			{
				using var writer = new StreamWriter(_currentLogFilePath, append: true, Encoding.UTF8);
				var pendingFlushCount = 0;
				var lastFlushAt = DateTime.UtcNow;

				while (_logQueue != null && (!_logQueue.IsCompleted || _logQueue.Count > 0))
				{
					if (!_logQueue.TryTake(out var entry, 50, _logWorkerCancellation?.Token ?? CancellationToken.None))
					{
						if (pendingFlushCount > 0 && DateTime.UtcNow - lastFlushAt >= FILE_FLUSH_INTERVAL)
						{
							await writer.FlushAsync();
							pendingFlushCount = 0;
							lastFlushAt = DateTime.UtcNow;
						}

						continue;
					}

					WriteToConsole(entry);
					await writer.WriteLineAsync(entry.Message);
					pendingFlushCount++;

					if (pendingFlushCount >= FILE_FLUSH_BATCH_SIZE
						|| entry.Level == LogLevel.ERROR
						|| DateTime.UtcNow - lastFlushAt >= FILE_FLUSH_INTERVAL)
					{
						await writer.FlushAsync();
						pendingFlushCount = 0;
						lastFlushAt = DateTime.UtcNow;
					}
				}

				if (pendingFlushCount > 0)
				{
					await writer.FlushAsync();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Logger: Background log processing failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Mirrors INFO/WARNING/ERROR entries to Godot's console output path.
		/// </summary>
		/// <param name="entry">Entry already formatted for file output.</param>
		private static void WriteToConsole(LogEntry entry)
		{
			if (entry.Level == LogLevel.DEBUG)
			{
				return;
			}

			if (entry.Level == LogLevel.ERROR)
			{
				GD.PushError(entry.Message);
				return;
			}

			GD.Print(entry.Message);
		}

		#endregion

		#region Retention

		/// <summary>
		/// Deletes older session logs according to the configured retention count.
		/// </summary>
		private void CleanupOldLogFiles()
		{
			try
			{
				var retentionCount = LOG_RETENTION_COUNT;
				if (retentionCount == -1)
				{
					return;
				}

				var absoluteLogsDir = ProjectSettings.GlobalizePath(ResolveLogsDirectory());
				var logFiles = Directory.GetFiles(absoluteLogsDir, "session_*.log");

				if (logFiles.Length <= retentionCount)
				{
					return;
				}

				Array.Sort(logFiles, (a, b) => File.GetCreationTime(a).CompareTo(File.GetCreationTime(b)));

				var filesToDelete = logFiles.Length - retentionCount;
				for (var i = 0; i < filesToDelete; i++)
				{
					try
					{
						File.Delete(logFiles[i]);
						GD.Print($"Logger: Deleted old log file: {Path.GetFileName(logFiles[i])}");
					}
					catch (Exception ex)
					{
						GD.PushError($"Logger: Failed to delete old log file {logFiles[i]}: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				GD.PushError($"Logger: Failed to cleanup old log files: {ex.Message}");
			}
		}

		/// <summary>
		/// Resolves the session-log directory, remapping into the automation userdata root when needed.
		/// </summary>
		/// <returns>Virtual Godot path for the sessions directory.</returns>
		private static string ResolveLogsDirectory()
		{
			return LOGS_DIRECTORY;
		}

		#endregion
	}
}
