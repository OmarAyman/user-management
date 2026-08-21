# One Dockerfile, four purposes: install once, then build the shipped image, run the unit suite, or run the
# browser suite. Named targets keep the dependency install shared instead of repeated three times.
#
# The build context is the repository root, not ./frontend, so every stage can reach certs/ - see the CA note
# in the deps stage. Source paths below are therefore prefixed with frontend/.

# ---------------------------------------------------------------------------------------------------------
# deps - node_modules from the lockfile, cached until the lockfile changes
# ---------------------------------------------------------------------------------------------------------
FROM node:24-bookworm-slim AS deps
WORKDIR /app

# The slim image ships no CA bundle tooling, and update-ca-certificates below needs it.
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*

# Optional corporate root CAs, before anything touches the network. Empty on a normal network, in which case
# this is a no-op; behind a TLS-inspecting proxy its absence makes `npm ci` fail on certificate validation.
# --use-openssl-ca points Node at the system store rather than its own bundled list. See certs/.gitkeep.
COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates
ENV NODE_OPTIONS=--use-openssl-ca

# Manifests only, so editing a component does not reinstall 671 packages.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

# ---------------------------------------------------------------------------------------------------------
# build - the production bundle
# ---------------------------------------------------------------------------------------------------------
FROM deps AS build
WORKDIR /app
COPY frontend/ ./
RUN npm run build

# ---------------------------------------------------------------------------------------------------------
# web - what actually ships: static files behind nginx, API proxied through the same origin
# ---------------------------------------------------------------------------------------------------------
FROM nginx:1.29-alpine AS web
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["/bin/sh", "-c", "wget -q -O- http://127.0.0.1/index.html >/dev/null || exit 1"]

# ---------------------------------------------------------------------------------------------------------
# unit - lint, the two rule-verification scripts, and the Vitest suite. No browser, no server, no database.
# ---------------------------------------------------------------------------------------------------------
FROM deps AS unit
WORKDIR /app
COPY frontend/ ./

# Chained with && so the first failure is the exit code: a lint error must not be reported as a passing run
# because the tests after it were green.
CMD ["sh", "-c", "npm run lint && npm run lint:verify && npm run lint:styles && npm test"]

# ---------------------------------------------------------------------------------------------------------
# e2e - the Playwright suite against a running stack. The image tag must match @playwright/test, or the
# browsers baked into it are not the ones the runner expects.
# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/playwright:v1.62.1-noble AS e2e
WORKDIR /app

COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates
ENV NODE_OPTIONS=--use-openssl-ca

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./

# Overridden by compose to point at the web container.
ENV E2E_BASE_URL=http://web

CMD ["npm", "run", "e2e"]
