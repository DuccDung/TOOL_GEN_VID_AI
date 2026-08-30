using System.IO;
using TOOL_LOCAL.AI;
using TOOL_LOCAL.AI.Contracts;

namespace TOOL_TESTS.AI;

public sealed class StructuredJsonParserTests
{
    [Fact]
    public void Parse_RejectsUnknownContractMembers()
    {
        const string json = """
        {
          "contentType": "educational",
          "primaryIntent": "inform",
          "targetAudience": "adults",
          "keywords": ["sleep"],
          "emotionalTriggers": ["curiosity"],
          "safetyRisks": [],
          "recommendedStoryStructure": "listicle",
          "unexpected": true
        }
        """;

        Assert.Throws<InvalidDataException>(() => new StructuredJsonParser().Parse<TopicAnalysisContract>(json));
    }
}
