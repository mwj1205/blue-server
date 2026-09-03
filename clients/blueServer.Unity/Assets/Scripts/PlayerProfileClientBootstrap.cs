using System;
using System.Threading;
using BlueServer.Client.Mail;
using BlueServer.Client.Network;
using BlueServer.Client.Protocol;
using UnityEngine;

namespace BlueServer.Client
{
    public sealed class PlayerProfileClientBootstrap : MonoBehaviour
    {
        private const string AccessTokenEnvironmentVariable =
            "BLUE_SERVER_ACCESS_TOKEN";

        private CancellationTokenSource _lifetimeCts;
        private GameTcpClient _client;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateClient()
        {
            var accessToken = Environment.GetEnvironmentVariable(
                AccessTokenEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Debug.LogWarning(
                    "BLUE_SERVER_ACCESS_TOKEN 환경 변수가 없어 TCP 연결을 건너뜁니다.");
                return;
            }

            var gameObject = new GameObject(
                nameof(PlayerProfileClientBootstrap));

            DontDestroyOnLoad(gameObject);

            var bootstrap = gameObject.AddComponent<PlayerProfileClientBootstrap>();
            bootstrap.ConnectAndLoadProfile(accessToken);
        }

        private async void ConnectAndLoadProfile(string accessToken)
        {
            _lifetimeCts = new CancellationTokenSource();
            _client = new GameTcpClient("127.0.0.1", 7777);

            try
            {
                await _client.ConnectAsync(_lifetimeCts.Token);

                var login = await _client.LoginAsync(
                    accessToken,
                    _lifetimeCts.Token);

                if (!login.Success)
                {
                    Debug.LogError(
                        "TCP 로그인 실패: " + login.Message);
                    return;
                }

                var profile = await _client.GetPlayerProfileAsync(
                    _lifetimeCts.Token);

                if (!profile.Success)
                {
                    Debug.LogError(
                        "플레이어 프로필 조회 실패: " + profile.Message);
                    return;
                }

                Debug.Log(
                    string.Format(
                        "프로필 조회 성공 - PlayerId={0}, Nickname={1}, Gold={2}, Gem={3}, Characters={4}, Parties={5}, ClearedStages={6}, TotalClears={7}",
                        profile.PlayerId,
                        profile.Nickname,
                        profile.Gold,
                        profile.Gem,
                        profile.OwnedCharacterCount,
                        profile.PartyCount,
                        profile.ClearedStageCount,
                        profile.TotalStageClearCount));

                var mailState = new MailInboxState(_client);
                var mailView = gameObject.AddComponent<MailInboxView>();
                mailView.Initialize(mailState, _lifetimeCts.Token);

                await mailState.LoadFirstPageAsync(
                    MailPacketCodec.DefaultPageSize,
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("플레이어 프로필 TCP 요청 취소");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void OnDestroy()
        {
            if (_lifetimeCts != null)
            {
                _lifetimeCts.Cancel();
            }

            if (_client != null)
            {
                _client.Dispose();
            }

            if (_lifetimeCts != null)
            {
                _lifetimeCts.Dispose();
            }
        }
    }
}
