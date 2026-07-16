using UnityEngine;

public class Quit : MonoBehaviour
{
    // 버튼 OnClick에 이 함수를 연결하세요.
    public void QuitGame()
    {
#if UNITY_EDITOR
        // 에디터에서 실행 중일 때는 플레이 모드를 종료합니다.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // 빌드된 게임에서는 애플리케이션을 종료합니다.
        Application.Quit();
#endif
    }
}