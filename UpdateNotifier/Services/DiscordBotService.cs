﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Commands;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Services;

public class DiscordBotService(
	DiscordSocketClient        client,
	InteractionService         interactionService,
	CommandHandler             commandHandler,
	Config                     config,
	ILogger<DiscordBotService> logger)
	: BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		client.Log += LogAsync;
		interactionService.Log += LogAsync;

		client.Ready += ReadyAsync;

		await client.LoginAsync(TokenType.Bot, config.BotToken);
		await client.StartAsync();

		// Block until the application is stopped
		await Task.Delay(Timeout.Infinite, stoppingToken);
	}

	private Task LogAsync(LogMessage log)
	{
		var severity = log.Severity switch
		{
			LogSeverity.Critical => LogLevel.Critical,
			LogSeverity.Error    => LogLevel.Error,
			LogSeverity.Warning  => LogLevel.Warning,
			LogSeverity.Info     => LogLevel.Information,
			LogSeverity.Debug    => LogLevel.Debug,
			LogSeverity.Verbose  => LogLevel.Trace,
			_                    => LogLevel.Debug,
		};

		logger.ZLog(severity, log.Exception, $"{log.Source}: {log.Message}");
		return Task.CompletedTask;
	}

	private async Task ReadyAsync()
	{
		logger.ZLog(LogLevel.Information, $"Discord client is connected and ready! Registering commands...");
		await commandHandler.InitializeAsync();
		await client.SetGameAsync("Monitoring game updates", type: ActivityType.Watching);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		logger.ZLogInformation($"Discord bot is shutting down...");

		await client.StopAsync();
		await client.LogoutAsync();

		await base.StopAsync(cancellationToken);
	}
}