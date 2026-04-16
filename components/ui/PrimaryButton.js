import React from 'react';
import { Text, TouchableOpacity, StyleSheet } from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { colors, radius, shadows } from '../../theme/tokens';

export default function PrimaryButton({
  label,
  onPress,
  style,
  gradientStyle,
  textStyle,
  disabled = false,
  colorsOverride,
}) {
  const gradientColors = colorsOverride || [colors.brandPrimary, colors.brandPrimaryDark];

  return (
    <TouchableOpacity
      style={[styles.button, style, disabled && styles.disabled]}
      onPress={onPress}
      activeOpacity={disabled ? 1 : 0.85}
      disabled={disabled}
    >
      <LinearGradient colors={gradientColors} style={[styles.gradient, gradientStyle]}>
        <Text style={[styles.label, textStyle]}>{label}</Text>
      </LinearGradient>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  button: {
    borderRadius: radius.pill,
    overflow: 'hidden',
    ...shadows.glowPrimary,
  },
  gradient: {
    minHeight: 44,
    paddingHorizontal: 18,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  label: {
    color: colors.bgDeep,
    fontSize: 16,
    fontWeight: '700',
  },
  disabled: {
    opacity: 0.75,
  },
});
