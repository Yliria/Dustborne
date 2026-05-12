using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 30f;
    public float currentHealth;

    [Header("Hit Feedback")]
    public Color hitFlashColor = Color.white;
    public float hitFlashDuration = 0.08f;

    private Renderer rend;
    private Material matInstance;
    private Color baseColor;
    private float hitFlashTimer;
    private bool dead;

    void Awake()
    {
        currentHealth = maxHealth;
        rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            matInstance = rend.material;
            baseColor = ReadColor(matInstance);
        }
    }

    void Update()
    {
        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            if (hitFlashTimer <= 0f) WriteColor(matInstance, baseColor);
        }
    }

    public bool IsAlive => !dead && currentHealth > 0f;

    public void TakeDamage(float amount)
    {
        if (dead) return;
        currentHealth -= amount;
        Flash();
        if (currentHealth <= 0f)
        {
            dead = true;
            Destroy(gameObject);
        }
    }

    private void Flash()
    {
        if (matInstance == null) return;
        WriteColor(matInstance, hitFlashColor);
        hitFlashTimer = hitFlashDuration;
    }

    private static Color ReadColor(Material m)
    {
        if (m == null) return Color.white;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        return m.color;
    }

    private static void WriteColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else m.color = c;
    }
}
