using Xunit;

namespace SharpBlueprint.Client.Tests
{
    public class EnvironmentTests
    {
        [Fact]
        public void GetFrameworkVersionTest()
        {
            var environment = new Environment();
            var version = environment.GetFrameworkVersion();

            Assert.True(".NET Framework 3.5".Equals(version));
        }
    }
}
