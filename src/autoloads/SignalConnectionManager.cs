using Godot;
using System;
using System.Collections.Generic;

namespace GodotDotnetTraining
{
    /// <summary>
    /// Manages signal handler connections for a Node, ensuring all handlers are
    /// disconnected when the owner node exits the scene tree. This prevents
    /// signal handler memory leaks from global signal buses outliving their subscribers.
    /// 
    /// Usage:
    ///   private SignalConnectionManager _signals;
    ///   
    ///   public override void _Ready()
    ///   {
    ///       _signals = new SignalConnectionManager(this);
    ///       _signals.Connect(() => SomeBus.Instance.SomeEvent += OnSome,
    ///                        () => SomeBus.Instance.SomeEvent -= OnSome);
    ///   }
    ///   
    ///   // All handlers are automatically disconnected when the node exits the tree.
    /// </summary>
    public class SignalConnectionManager
    {
        private readonly List<(Action connect, Action disconnect)> _connections = new();
        private readonly Node _owner;
        private readonly Callable _treeEnteredCallable;
        private readonly Callable _treeExitingCallable;
        private bool _isConnected;
        private bool _lifecycleHooksConnected;

        public SignalConnectionManager(Node owner)
        {
            _owner = owner;
            _treeEnteredCallable = Callable.From((Action)ReconnectAll);
            _treeExitingCallable = Callable.From((Action)OnOwnerTreeExiting);
            _owner.Connect(Node.SignalName.TreeEntered, _treeEnteredCallable);
            _owner.Connect(Node.SignalName.TreeExiting, _treeExitingCallable);
            _lifecycleHooksConnected = true;
            _isConnected = _owner.IsInsideTree();
        }

        /// <summary>
        /// Subscribes via <paramref name="connect"/> and registers
        /// <paramref name="disconnect"/> for automatic cleanup on tree exit.
        /// </summary>
        public void Connect(Action connect, Action disconnect)
        {
            _connections.Add((connect, disconnect));

            if (_owner.IsInsideTree())
            {
                connect();
                _isConnected = true;
            }
        }

        /// <summary>
        /// Disconnects all tracked signal handlers and clears the list.
        /// Safe to call multiple times.
        /// </summary>
        public void DisconnectAll()
        {
            DisconnectTrackedConnections(clearConnections: true);
            DisconnectLifecycleHooks();
        }

        private void OnOwnerTreeExiting()
        {
            DisconnectTrackedConnections(clearConnections: false);
        }

        private void ReconnectAll()
        {
            if (_isConnected)
            {
                return;
            }

            foreach (var (connect, _) in _connections)
            {
                connect();
            }

            _isConnected = true;
        }

        private void DisconnectTrackedConnections(bool clearConnections)
        {
            if (_isConnected)
            {
                foreach (var (_, disconnect) in _connections)
                {
                    try
                    {
                        disconnect();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Source object already disposed — safe to ignore
                    }
                }
            }

            _isConnected = false;

            if (clearConnections)
            {
                _connections.Clear();
            }
        }

        private void DisconnectLifecycleHooks()
        {
            if (!_lifecycleHooksConnected || !GodotObject.IsInstanceValid(_owner))
            {
                _lifecycleHooksConnected = false;
                return;
            }

            if (_owner.IsConnected(Node.SignalName.TreeEntered, _treeEnteredCallable))
            {
                _owner.Disconnect(Node.SignalName.TreeEntered, _treeEnteredCallable);
            }

            if (_owner.IsConnected(Node.SignalName.TreeExiting, _treeExitingCallable))
            {
                _owner.Disconnect(Node.SignalName.TreeExiting, _treeExitingCallable);
            }

            _lifecycleHooksConnected = false;
        }
    }
}
