﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core.Skills;
using Microsoft.Bot.Builder.Skills;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Microsoft.BotBuilderSamples.SimpleRootBot31.Bots
{
    public class RootBot : ActivityHandler
    {
        private readonly IStatePropertyAccessor<BotFrameworkSkill> _activeSkillProperty;
        private readonly string _botId;
        private readonly ICredentialProvider _credentialProvider;
        private readonly ConversationState _conversationState;
        private readonly SkillHttpClient _skillClient;
        private readonly SkillsConfiguration _skillsConfig;
        private readonly BotFrameworkSkill _targetSkill;

        public RootBot(ConversationState conversationState, SkillsConfiguration skillsConfig, SkillHttpClient skillClient, IConfiguration configuration, ICredentialProvider credentialProvider)
        {
            _conversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));
            _skillsConfig = skillsConfig ?? throw new ArgumentNullException(nameof(skillsConfig));
            _skillClient = skillClient ?? throw new ArgumentNullException(nameof(skillsConfig));
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            _credentialProvider = credentialProvider;
            if (_credentialProvider.IsAuthenticationDisabledAsync().Result)
            {
                throw new ArgumentException("No MicrosoftAppIds found in configuration. Auth is required for Skills.");
            }

            // We use a single skill in this example.
            var targetSkillId = "EchoSkillBot";
            if (!_skillsConfig.Skills.TryGetValue(targetSkillId, out _targetSkill))
            {
                throw new ArgumentException($"Skill with ID \"{targetSkillId}\" not found in configuration");
            }

            // Create state property to track the active skill
            _activeSkillProperty = conversationState.CreateProperty<BotFrameworkSkill>("activeSkillProperty");
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            // Try to get the active skill
            var activeSkill = await _activeSkillProperty.GetAsync(turnContext, () => null, cancellationToken);

            if (activeSkill != null)
            {
                // Send the activity to the skill
                await SendToSkill(turnContext, activeSkill, cancellationToken);
                return;
            }

            if (turnContext.Activity.Text.Contains("skill"))
            {
                await turnContext.SendActivityAsync(MessageFactory.Text("Got it, connecting you to the skill..."), cancellationToken);

                // Save active skill in state
                await _activeSkillProperty.SetAsync(turnContext, _targetSkill, cancellationToken);

                // Send the activity to the skill
                await SendToSkill(turnContext, _targetSkill, cancellationToken);
                return;
            }

            // just respond
            await turnContext.SendActivityAsync(MessageFactory.Text("Me no nothin'. Say \"skill\" and I'll patch you through"), cancellationToken);

            // Save conversation state
            await _conversationState.SaveChangesAsync(turnContext, force: true, cancellationToken: cancellationToken);
        }

        protected override async Task OnEndOfConversationActivityAsync(ITurnContext<IEndOfConversationActivity> turnContext, CancellationToken cancellationToken)
        {
            // forget skill invocation
            await _activeSkillProperty.DeleteAsync(turnContext, cancellationToken);

            // Show status message, text and value returned by the skill
            var eocActivityMessage = $"Received {ActivityTypes.EndOfConversation}.\n\nCode: {turnContext.Activity.Code}";
            if (!string.IsNullOrWhiteSpace(turnContext.Activity.Text))
            {
                eocActivityMessage += $"\n\nText: {turnContext.Activity.Text}";
            }

            if ((turnContext.Activity as Activity)?.Value != null)
            {
                eocActivityMessage += $"\n\nValue: {JsonConvert.SerializeObject((turnContext.Activity as Activity)?.Value)}";
            }

            await turnContext.SendActivityAsync(MessageFactory.Text(eocActivityMessage), cancellationToken);

            // We are back at the root
            await turnContext.SendActivityAsync(MessageFactory.Text("Back in the root bot. Say \"skill\" and I'll patch you through"), cancellationToken);

            // Save conversation state
            await _conversationState.SaveChangesAsync(turnContext, cancellationToken: cancellationToken);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Hello and welcome! (dotnet core 3.1)"), cancellationToken);
                }
            }
        }

        private async Task SendToSkill(ITurnContext<IMessageActivity> turnContext, BotFrameworkSkill targetSkill, CancellationToken cancellationToken)
        {
            // NOTE: Always SaveChanges() before calling a skill so that any activity generated by the skill
            // will have access to current accurate state.
            await _conversationState.SaveChangesAsync(turnContext, force: true, cancellationToken: cancellationToken);

            // Get the botId from the claims for posting an Activity to the skill.
            var botId = await GetValidBotId(turnContext);

            // route the activity to the skill
            var response = await _skillClient.PostActivityAsync(botId, targetSkill, _skillsConfig.SkillHostEndpoint, (Activity)turnContext.Activity, cancellationToken);

            // Check response status
            if (!(response.Status >= 200 && response.Status <= 299))
            {
                throw new HttpRequestException($"Error invoking the skill id: \"{targetSkill.Id}\" at \"{targetSkill.SkillEndpoint}\" (status is {response.Status}). \r\n {response.Body}");
            }
        }

        private async Task<string> GetValidBotId(ITurnContext turnContext)
        {
            var identity = turnContext.TurnState.Get<ClaimsIdentity>("BotIdentity");
            var audienceClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.AudienceClaim)?.Value;

            if (await _credentialProvider.IsValidAppIdAsync(audienceClaim))
            {
                return audienceClaim;
            }

            throw new ArgumentException("No MicrosoftAppIds found in configuration. Auth is required for Skills.");
        }
    }
}
