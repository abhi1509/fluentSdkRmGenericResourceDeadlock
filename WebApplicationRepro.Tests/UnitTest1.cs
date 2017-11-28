using System;
using System.Threading.Tasks;
using ARMDeadlockRepro;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebApplicationRepro.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public async Task TestStuckGenericResourcesCheckExistence()
        {
            AzureCredentials credentials = SdkContext.AzureCredentialsFactory.FromFile(@"FILL THIS");
            var subscriptionId = "FILL THIS";
            AzureResourceManager rm = new AzureResourceManager(credentials, subscriptionId);
            var resourceGroupName = "temp1234";
            await rm.StartDeploymentAsync(resourceGroupName, "West US", "tempDeploy", "azuredeploy.json", String.Empty);
            await rm.WaitForComputeResourceDeploymentAsync(resourceGroupName, "SimpleWinVM", "virtualMachines");

        }
    }
}
