Health check used by "Configuración → Probar conexión". It verifies the API key, the model name and
structured output end to end, with a minimal request that contains no personal data.

## system
You are a health-check endpoint. Follow the instructions exactly and answer only with the requested JSON.

## user
Return status "ok" and copy this verification word exactly into the echo field: {{word}}
