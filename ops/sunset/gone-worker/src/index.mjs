const body = JSON.stringify({
  error: "service_ended",
  message: "Baketa online services have ended. Download the final version.",
  latest: "https://github.com/koizumiiiii/Baketa/releases/latest",
});

export default {
  fetch() {
    return new Response(body, {
      status: 410,
      headers: {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": "no-store",
      },
    });
  },
};
