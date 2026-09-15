namespace TOOL_TESTS.TikTok;

public static class TikTokUploadUrlCases
{
    public static IEnumerable<object[]> RejectedUrls()
    {
        yield return ["/video/?upload_id=test"];
        yield return ["http://open-upload-sg.tiktokapis.com/video/?upload_id=test"];
        yield return ["https://open-upload-sg.tiktokapis.com:8443/video/?upload_id=test"];
        yield return ["https://open-upload-sg.tiktokapis.com.attacker.invalid/video/?upload_id=test"];
        yield return ["https://fake-open-upload-sg.tiktokapis.com/video/?upload_id=test"];
        yield return ["https://nested.open-upload-sg.tiktokapis.com/video/?upload_id=test"];
        yield return ["https://user@open-upload-sg.tiktokapis.com/video/?upload_id=test"];
        yield return ["https://open-upload-sg.tiktokapis.com/video/?upload_id=test#fragment"];
        yield return ["https://open-upload-sg.tiktokapis.com/video/?upload_token=" + new string('x', 2049)];
    }
}
