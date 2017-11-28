// <copyright>
// Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Models;
using Newtonsoft.Json.Linq;

namespace ARMDeadlockRepro
{
    public class AzureResourceManager
    {
        private const int DeploymentDelayMilliseconds = 5000;
        private readonly IResourceManager _resourceManager;

        public AzureResourceManager(AzureCredentials credentials, string subscriptionId)
        {
            _resourceManager = ResourceManager
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(credentials)
                .WithSubscription(subscriptionId);
        }

        public async Task<bool> TryCreateResourceGroupAsync(string resourceGroupName, string location)
        {
            if (await DoesResourceGroupExistAsync(resourceGroupName))
            {
                return false;
            }

            await _resourceManager
                .ResourceGroups
                .Define(resourceGroupName)
                .WithRegion(location)
                .CreateAsync()
                .ConfigureAwait(false);


            return true;
        }

        public async Task DeleteResourceGroupAsync(string resourceGroupName)
        {
            {
                if (await DoesResourceGroupExistAsync(resourceGroupName))
                {
                    await _resourceManager
                        .ResourceGroups
                        .DeleteByNameAsync(resourceGroupName)
                        .ConfigureAwait(false);
                }
            }
        }

        public Task<bool> DoesResourceGroupExistAsync(string resourceGroupName)
        {
            return _resourceManager
                .ResourceGroups
                .CheckExistenceAsync(resourceGroupName);
        }


        public async Task<bool> StartDeploymentAsync(string resourceGroupName, string location, string deploymentName, string templateFilePath, string parameters)
        {
            string armTemplate = GetARMTemplate(templateFilePath);

            await TryCreateResourceGroupAsync(resourceGroupName, location);

            if (_resourceManager.Deployments.CheckExistence(resourceGroupName,
                deploymentName))
            {
                IDeployment deployment = await _resourceManager
                    .Deployments
                    .GetByResourceGroupAsync(resourceGroupName, deploymentName)
                    .ConfigureAwait(false);

                ProvisioningState provisioningState = ProvisioningState.Parse(deployment.ProvisioningState);

                if (provisioningState.DeploymentRunning())
                {
                    return false;
                }
            }

            _resourceManager
                .Deployments
                .Define(deploymentName)
                .WithExistingResourceGroup(resourceGroupName)
                .WithTemplate(armTemplate)
                .WithParameters(parameters)
                .WithMode(DeploymentMode.Incremental)
                .BeginCreate();

            return true;
        }

        public async Task<bool> WaitForComputeResourceDeploymentAsync(string resourceGroupName, string resourceName,
            string resourceType)
        {
            bool resourceExists = false;
            while (!resourceExists)
            {
                resourceExists = _resourceManager.GenericResources.CheckExistence(resourceGroupName,
                    "Microsoft.Compute", String.Empty, resourceType, resourceName, "2016-04-30-preview");

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            IGenericResource computeResource = GetComputeResource(resourceGroupName, resourceName, resourceType);
            ProvisioningState provisioningState =
                ProvisioningState.Parse((string)JObject.FromObject(computeResource.Properties)["provisioningState"]);

            while (!provisioningState.DeploymentEnded())
            {

                await SdkContext.DelayProvider.DelayAsync(DeploymentDelayMilliseconds, CancellationToken.None);
                computeResource = GetComputeResource(resourceGroupName, resourceName, resourceType);

                provisioningState =
                    ProvisioningState.Parse(
                        (string)JObject.FromObject(computeResource.Properties)["provisioningState"]);
            }

            return provisioningState.DeploymentSucceeded();
        }

        private IGenericResource GetComputeResource(string resourceGroupName, string resourceName, string resourceType)
        {
            return _resourceManager.GenericResources.Get(
                resourceGroupName: resourceGroupName,
                resourceProviderNamespace: "Microsoft.Compute",
                parentResourcePath: string.Empty,
                resourceType: resourceType,
                resourceName: resourceName,
                apiVersion: "2016-04-30-preview");
        }

        private static string GetARMTemplate(string templateFileName)
        {
            string armTemplateString = File.ReadAllText(templateFileName);
            JObject parsedTemplate = JObject.Parse(armTemplateString);
            return parsedTemplate.ToString();
        }

        private class ProvisioningState : ExpandableStringEnum<ProvisioningState>
        {
            // Provisioning states as documented at https://msdn.microsoft.com/en-us/library/azure/microsoft.azure.management.resources.models.AzureResourceManagerProvisioningState_members.aspx
            private static readonly ProvisioningState Accepted = Parse("Accepted ");

            private static readonly ProvisioningState Canceled = Parse("Canceled");

            private static readonly ProvisioningState Created = Parse("Created ");

            private static readonly ProvisioningState Creating = Parse("Creating");

            private static readonly ProvisioningState Deleted = Parse("Deleted");

            private static readonly ProvisioningState Deleting = Parse("Deleting");

            private static readonly ProvisioningState Failed = Parse("Failed");

            private static readonly ProvisioningState NotSpecified = Parse("NotSpecified");

            private static readonly ProvisioningState Registering = Parse("Registering");

            private static readonly ProvisioningState Running = Parse("Running");

            private static readonly ProvisioningState Succeeded = Parse("Succeeded");

            public bool DeploymentRunning()
            {
                ProvisioningState state = Parse(Value);
                return state == Creating ||
                       state == Registering ||
                       state == Accepted ||
                       state == Running;
            }

            public bool DeploymentEnded()
            {
                ProvisioningState state = Parse(Value);
                return state == Succeeded ||
                       state == Canceled ||
                       state == Failed;
            }

            public bool DeploymentSucceeded()
            {
                ProvisioningState state = Parse(Value);
                return state == Succeeded;
            }
        }
    }
}
