# Deployment Guide

## Prerequisites

- Docker & Docker Compose installed
- Nginx installed (`sudo apt install nginx`)
- Certbot installed (`sudo apt install certbot`)
- `.env` file with required variables (see `docker-compose.prod.yml`)

## 1. Start the Application

```bash
docker compose -f docker-compose.prod.yml up -d
```

Verify it's running:

```bash
curl -I http://127.0.0.1:8080
```

## 2. Set Up HTTP Nginx Config (for Certbot)

```bash
sudo mkdir -p /var/www/certbot
sudo ln -s $(pwd)/deploy/nginx/elisamtz.nutrir.ca.http.conf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

Verify HTTP proxy works:

```bash
curl -I http://elisamtz.nutrir.ca
```

## 3. Obtain SSL Certificate

```bash
sudo certbot certonly --webroot -w /var/www/certbot -d elisamtz.nutrir.ca
```

## 4. Switch to HTTPS Nginx Config

```bash
sudo rm /etc/nginx/sites-enabled/elisamtz.nutrir.ca.http.conf
sudo ln -s $(pwd)/deploy/nginx/elisamtz.nutrir.ca.conf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

Verify HTTPS works:

```bash
curl -I https://elisamtz.nutrir.ca
```

## 5. Verify Auto-Renewal

```bash
sudo certbot renew --dry-run
```

Certbot installs a systemd timer by default. Verify it's active:

```bash
sudo systemctl status certbot.timer
```

## Updating the Application

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

---

# Static Site: nutrir.ca

## 1. Create Site Root and Add Content

```bash
sudo mkdir -p /var/www/nutrir.ca
# Place your index.html and static assets here
```

## 2. Set Up HTTP Nginx Config (for Certbot)

```bash
sudo ln -s $(pwd)/deploy/nginx/nutrir.ca.http.conf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

## 3. Obtain SSL Certificate

```bash
sudo certbot certonly --webroot -w /var/www/certbot -d nutrir.ca -d www.nutrir.ca
```

## 4. Switch to HTTPS Nginx Config

```bash
sudo rm /etc/nginx/sites-enabled/nutrir.ca.http.conf
sudo ln -s $(pwd)/deploy/nginx/nutrir.ca.conf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

---

# Notes

## `map` Directive

The elisamtz nginx configs include a `map $http_upgrade $connection_upgrade` block for WebSocket support. If you have multiple sites using this directive, move it to `/etc/nginx/conf.d/websocket-map.conf` and remove it from individual site configs to avoid duplicates.
