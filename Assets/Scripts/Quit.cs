using UnityEngine;

public class Quit : MonoBehaviour
{
    // ��ư OnClick�� �� �Լ��� �����ϼ���.
    public void QuitGame()
    {
#if UNITY_EDITOR
        // �����Ϳ��� ���� ���� ���� �÷��� ��带 �����մϴ�.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // ���� ���ӿ����� ���ø����̼��� �����մϴ�.
        Application.Quit();
#endif
    }
}