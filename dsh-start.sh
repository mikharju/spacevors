dsh web \
  --host 127.0.0.1 \
  --port 3079 \
  --trusted-host localhost:3080 \
  --no-open \
  > /home/agent/dsh.log 2>&1 &

sleep 2

nginx \
  -c /home/agent/nginx/nginx.conf \
  -p /home/agent/nginx \
  -g 'daemon off;' \
  > /home/agent/nginx.log 2>&1 &
