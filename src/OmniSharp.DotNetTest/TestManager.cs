﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
using OmniSharp.Services;
using OmniSharp.Utilities;

namespace OmniSharp.DotNetTest
{
    public abstract class TestManager : DisposableObject
    {
        protected readonly DotNetCliService DotNetCli;
        protected readonly string WorkingDirectory;
        protected readonly ILogger Logger;

        private Process _process;
        private Socket _socket;
        private NetworkStream _stream;
        private BinaryReader _reader;
        private BinaryWriter _writer;

        protected TestManager(DotNetCliService dotNetCli, string workingDirectory, ILogger logger)
        {
            DotNetCli = dotNetCli ?? throw new ArgumentNullException(nameof(dotNetCli));
            WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override void DisposeCore(bool disposing)
        {
            if (_process?.HasExited == false)
            {
                _process.KillChildrenAndThis();
            }

            if (_process != null)
            {
                _process = null;
            }

            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }

            if (_writer != null)
            {
                _writer.Dispose();
                _writer = null;
            }

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }
        }

        protected abstract string GetCliTestArguments(int port, int parentProcessId);
        protected abstract void VersionCheck();

        protected void Connect()
        {
            var port = FindFreePort();

            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
            listener.Listen(1);

            var currentProcess = Process.GetCurrentProcess();
            _process = DotNetCli.Start(GetCliTestArguments(port, currentProcess.Id), WorkingDirectory);

            _socket = listener.Accept();
            _stream = new NetworkStream(_socket);
            _reader = new BinaryReader(_stream);
            _writer = new BinaryWriter(_stream);

            // Read the initial "connected" response
            var message = ReadMessage();

            if (message.MessageType != MessageType.SessionConnected)
            {
                throw new InvalidOperationException($"Expected {MessageType.SessionConnected} but was {message.MessageType}");
            }

            VersionCheck();
        }

        private static int FindFreePort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        protected Message ReadMessage()
        {
            var rawMessage = _reader.ReadString();
            Logger.LogInformation($"read: {rawMessage}");

            return JsonDataSerializer.Instance.DeserializeMessage(rawMessage);
        }

        protected void SendMessage(string messageType)
        {
            var rawMessage = JsonDataSerializer.Instance.SerializePayload(messageType, new object());
            Logger.LogInformation($"send: {rawMessage}");

            _writer.Write(rawMessage);
        }

        protected void SendMessage<T>(string messageType, T payload)
        {
            var rawMessage = JsonDataSerializer.Instance.SerializePayload(messageType, payload);
            Logger.LogInformation($"send: {rawMessage}");

            _writer.Write(rawMessage);
        }
    }
}
