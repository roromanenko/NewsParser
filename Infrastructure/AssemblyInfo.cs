using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Infrastructure.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")] // required for Moq to mock internal interfaces
