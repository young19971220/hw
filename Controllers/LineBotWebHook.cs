using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using isRock.LineBot;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace LineWebHook.Controllers {

    [Route ("api/[controller]")]
    [ApiController]
    public class LineBotWebHookController : ControllerBase {
        string _channelAccessToken { get; set; }
        string _channelSecret { get; set; }
        //string _toUserId { get; set; }
        Bot bot { get; set; }

        public LineBotWebHookController (IConfiguration config) {
            _channelSecret = config.GetSection ("LineBotSettings:channelSecret").Value;
            _channelAccessToken = config.GetSection ("LineBotSettings:channelAccessToken").Value;
            //_toUserId = config.GetSection ("LineBotSettings:toUserId").Value;

            bot = new Bot (_channelAccessToken);
        }

        [HttpPost]
        public async Task<IActionResult> Post () {
            StatusCodeResult actionResult = Unauthorized ();

            var postData = await getPostDataAsync(Request, _channelSecret);

            if (!string.IsNullOrEmpty(postData)) {
                var received = Utility.Parsing (postData);

                //should enumerate each event in reveived.events, but this is just a practice, do it simple.
                var lineEvent = received.events.FirstOrDefault ();

                // get replyToken from lineEvent
                var replyToken = lineEvent.replyToken;

                // "0x32" is token for line verify
                if (lineEvent.replyToken != "00000000000000000000000000000000") {
                    switch (lineEvent.type) {
                        case "join":
                            bot.ReplyMessage (replyToken, $"有人把我加入 {lineEvent.source.type} 中了，大家好啊～");
                            actionResult = Ok ();
                            break;

                        case "message":
                            // reserve this block  for non-stop conversation
                            // if (lineEvent.message.text == "我要請假") {} else {}
                            replyMessage (lineEvent, replyToken);
                            actionResult = Ok ();
                            break;

                        default:
                            actionResult = BadRequest ();
                            break;
                    }
                } else {
                    actionResult = Ok ();
                }
            }

            return actionResult;
        }

        private async Task<string> getPostDataAsync (HttpRequest request, string secret) {
            var postData = string.Empty;

            using (var reader = new StreamReader(request.Body)) {
                postData = await reader.ReadToEndAsync();
            }

            var utf8       = new UTF8Encoding();
            var dataBuffer = utf8.GetBytes (postData);
            var key        = utf8.GetBytes (secret);
            var digest     = string.Empty;

            using (var hmacSha256 = new HMACSHA256 (key)) {
                var hash = hmacSha256.ComputeHash (dataBuffer);
                digest = Convert.ToBase64String (hash);
            };

            if (request.Headers["X-Line-Signature"] != digest) {
                postData = string.Empty;
            }

            return postData;
        }

        private void replyMessage (Event lineEvent, string replyToken) {
            var eventMessage  = lineEvent.message;
            var eventSource   = lineEvent.source;
            var sourceUserId  = eventSource.userId;
            var sourceRoomId  = eventSource.roomId;
            var sourceGroupId = eventSource.groupId;

            switch (lineEvent.message.type) {
                case "text":
                    var messageText = eventMessage.text;
                    var sourceType  = eventSource.type.ToLower();

                    if (messageText != "bye") {
                        LineUserInfo userInfo = null;

                        switch (sourceType) {
                            case "room":
                                userInfo = Utility.GetRoomMemberProfile(sourceRoomId, sourceUserId, _channelAccessToken);
                                break;

                            case "group":
                                userInfo = Utility.GetGroupMemberProfile(sourceGroupId, sourceUserId, _channelAccessToken);
                                break;

                            case "user":
                                userInfo = Utility.GetUserInfo(sourceUserId, _channelAccessToken);
                                break;

                            default:
                                break;
                        }

                        bot.ReplyMessage(replyToken, "你說了：" + messageText + "\n你是：" + userInfo.displayName);
                    }
                    else {
                        bot.ReplyMessage(replyToken, "bye-bye");

                        if (sourceType == "room")
                            Utility.LeaveRoom(sourceRoomId, _channelAccessToken);

                        if (sourceType == "group")
                            Utility.LeaveGroup(sourceGroupId, _channelAccessToken);
                    }

                    break;

                case "sticker":
                    bot.ReplyMessage (replyToken, eventMessage.packageId, eventMessage.stickerId);
                    break;

                default:
                    break;
            }
        }
    }
}