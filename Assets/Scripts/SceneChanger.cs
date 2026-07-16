using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    // Inspector에서 이동할 씬 이름을 지정하세요. (Build Settings에 씬이 등록되어 있어야 합니다.)
    [SerializeField] private string sceneName;

    // 버튼 OnClick에 이 함수를 연결하면 sceneName에 지정된 씬으로 이동합니다.
    public void ChangeScene()
    {
        SceneManager.LoadScene(sceneName);
    }

    // 버튼 OnClick에서 직접 씬 이름을 넘겨 이동할 때 사용합니다.
    public void ChangeScene(string targetScene)
    {
        SceneManager.LoadScene(targetScene);
    }

    // 씬 빌드 인덱스로 이동할 때 사용합니다.
    public void ChangeScene(int buildIndex)
    {
        SceneManager.LoadScene(buildIndex);
    }
}
