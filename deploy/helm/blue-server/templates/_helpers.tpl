{{/* Chart 이름 확장 */}}
{{- define "blue-server.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Kubernetes 이름 길이를 고려한 전체 application 이름 생성 */}}
{{- define "blue-server.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/* Chart label용 이름과 버전 생성 */}}
{{- define "blue-server.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* 공통 metadata label */}}
{{- define "blue-server.commonLabels" -}}
helm.sh/chart: {{ include "blue-server.chart" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: blue-server
{{- end }}

{{/* Application 공통 label */}}
{{- define "blue-server.labels" -}}
{{ include "blue-server.commonLabels" . }}
{{ include "blue-server.selectorLabels" . }}
{{- end }}

{{/* Application 공통 selector label */}}
{{- define "blue-server.selectorLabels" -}}
app.kubernetes.io/name: {{ include "blue-server.name" . }}
app.kubernetes.io/instance: {{ default .Release.Name .Values.global.instanceName }}
{{- end }}

{{/* 공통 ConfigMap 이름 */}}
{{- define "blue-server.configMapName" -}}
{{- printf "%s-config" (include "blue-server.fullname" .) | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Orleans Silo resource 이름 */}}
{{- define "blue-server.siloName" -}}
{{- printf "%s-silo" (include "blue-server.fullname" .) | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Orleans Silo selector label */}}
{{- define "blue-server.siloSelectorLabels" -}}
app.kubernetes.io/name: {{ include "blue-server.siloName" . }}
app.kubernetes.io/instance: {{ default .Release.Name .Values.global.instanceName }}
{{- end }}

{{/* Orleans Silo 공통 label */}}
{{- define "blue-server.siloLabels" -}}
{{ include "blue-server.commonLabels" . }}
{{ include "blue-server.siloSelectorLabels" . }}
app.kubernetes.io/component: silo
{{- end }}

{{/* Orleans Silo ServiceAccount 이름 */}}
{{- define "blue-server.siloServiceAccountName" -}}
{{- default (include "blue-server.siloName" .) .Values.silo.serviceAccountName | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* API resource 이름 */}}
{{- define "blue-server.apiName" -}}
{{- printf "%s-api" (include "blue-server.fullname" .) | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* API selector label */}}
{{- define "blue-server.apiSelectorLabels" -}}
app.kubernetes.io/name: {{ include "blue-server.apiName" . }}
app.kubernetes.io/instance: {{ default .Release.Name .Values.global.instanceName }}
{{- end }}

{{/* API 공통 label */}}
{{- define "blue-server.apiLabels" -}}
{{ include "blue-server.commonLabels" . }}
{{ include "blue-server.apiSelectorLabels" . }}
app.kubernetes.io/component: api
{{- end }}

{{/* Game TCP server resource 이름 */}}
{{- define "blue-server.gameName" -}}
{{- printf "%s-game" (include "blue-server.fullname" .) | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Game TCP server selector label */}}
{{- define "blue-server.gameSelectorLabels" -}}
app.kubernetes.io/name: {{ include "blue-server.gameName" . }}
app.kubernetes.io/instance: {{ default .Release.Name .Values.global.instanceName }}
{{- end }}

{{/* Game TCP server 공통 label */}}
{{- define "blue-server.gameLabels" -}}
{{ include "blue-server.commonLabels" . }}
{{ include "blue-server.gameSelectorLabels" . }}
app.kubernetes.io/component: game-server
{{- end }}
