FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY consumer.py .
COPY consumer_logic.py .
COPY kafka_emit.py .
CMD ["python", "-u", "consumer.py"]
