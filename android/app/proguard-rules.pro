# Room generates code reflectively accessed by the annotation processor's
# generated implementations; keep entities/DAOs' public surface intact.
-keep class com.sunopesmestudio.app.data.** { *; }
