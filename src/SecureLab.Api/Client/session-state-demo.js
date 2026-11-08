import { apiFetch } from "./api-fetch.js";

const result = document.querySelector("#result");
function showState(state) {
  for (const id of ["anonymous", "signed-in", "unknown"]) {
    document.querySelector(`#${id}`).hidden = id !== state;
  }
}
async function refreshSessionState() {
  const response = await apiFetch("/api/auth/session-check");
  if (response.status === 204) {
    showState("signed-in");
  } else if (response.status === 401) {
    showState("anonymous");
  } else {
    showState("unknown");
    throw new Error(`Перевірка сеансу: ${response.status}`);
  }
}
async function refreshSafely() {
  try {
    await refreshSessionState();
  } catch {
    showState("unknown");
    result.textContent = "Не вдалося перевірити сеанс. Повторіть запит.";
  }
}
document.querySelector("#login").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  form.elements.password.value = "";
  try {
    const response = await apiFetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    });
    result.textContent = `Login: ${response.status}`;


    if (response.status === 204) {
        SecureLabCsrf.reset();
        await loadProfile();
    }

  } catch {
    result.textContent = "Помилка мережі під час входу.";
  } finally {
    delete data.password;
    await refreshSafely();
  }
});
document.querySelector("#logout").addEventListener("click", async () => {
  try {
      const response = await SecureLabCsrf.mutate(
          "/api/auth/logout",
          {
              method: "POST"
          }
      );
    result.textContent = `Logout: ${response.status}`;
      if (response.status === 204) {
          SecureLabCsrf.reset();

          profileResult.textContent = "";
          incidentResult.textContent = "";
      }

  } catch {
    result.textContent = "Помилка мережі під час виходу.";
  } finally {
    await refreshSafely();
  }
});
await refreshSafely();

const registerForm = document.querySelector("#register-form");
const incidentForm = document.querySelector("#incident-form");
const profileResult = document.querySelector("#profile-result");
const incidentResult = document.querySelector("#incident-result");

async function loadProfile() {
  profileResult.textContent = "";

  try {
    const response = await apiFetch("/api/me");

    if (response.status === 401) {
      profileResult.textContent = "Не ввійшли.";
      return;
    }

    if (!response.ok) {
      profileResult.textContent =
        `Помилка профілю: ${response.status}`;
      return;
    }

    const profile = await response.json();

    profileResult.textContent =
      `Користувач: ${profile.userName}\n` +
      `Роль: ${profile.role}\n` +
      `ID: ${profile.userId}`;
  } catch {
    profileResult.textContent = "Помилка мережі.";
  }
}

document.querySelector("#profile-button")
  .addEventListener("click", loadProfile);

registerForm.addEventListener("submit", async event => {
  event.preventDefault();

  const form = new FormData(registerForm);

  const data = {
    userName: form.get("userName"),
    email: form.get("email"),
    displayName: form.get("displayName"),
    password: form.get("password")
  };

  registerForm.elements.namedItem("password").value = "";

  try {
    const response = await apiFetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    });

    document.querySelector("#register-result").textContent =
      response.status === 201
        ? "Реєстрація успішна. Тепер увійдіть."
        : `Реєстрацію відхилено: ${response.status}`;
  } catch {
    document.querySelector("#register-result").textContent =
      "Помилка мережі.";
  } finally {
    data.password = undefined;
  }
});

incidentForm.addEventListener("submit", async event => {
  event.preventDefault();

  const form = new FormData(incidentForm);

  const data = {
    title: form.get("title"),
    description: form.get("description"),
    severity: form.get("severity"),
    occurredAtUtc:
      new Date(form.get("occurredAtUtc")).toISOString()
  };

  try {
      const response = await SecureLabCsrf.mutate("/api/incidents", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    });

    incidentResult.textContent =
      response.status === 201
        ? "Інцидент створено."
        : `Помилка створення: ${response.status}`;
  } catch {
    incidentResult.textContent = "Помилка мережі.";
  }
});

