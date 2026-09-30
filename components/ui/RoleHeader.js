import React from 'react';
import { Text, TouchableOpacity, View, StyleSheet } from 'react-native';

export default function RoleHeader({
  title,
  subtitle,
  onMenuPress,
  onLeftPress,
  leftIcon = '›',
  leftLabel,
  showLeftButton = true,
  onRightPress,
  rightIcon = '⌕',
  rightLabel,
  showRightButton = true,
  theme = 'light',
  titleStyle,
}) {
  const leftPressHandler = onLeftPress || onMenuPress;
  const isDark = theme === 'dark';

  return (
    <View style={[styles.header, isDark ? styles.headerDark : null]}>
      {showLeftButton ? (
        <TouchableOpacity
          activeOpacity={0.75}
          style={[styles.iconButton, isDark ? styles.iconButtonDark : null]}
          onPress={leftPressHandler}
          disabled={!leftPressHandler}
        >
          {!!leftIcon && <Text style={[styles.headerIcon, isDark ? styles.headerIconDark : null]}>{leftIcon}</Text>}
          {!!leftLabel && <Text style={[styles.leftLabel, isDark ? styles.leftLabelDark : null]}>{leftLabel}</Text>}
        </TouchableOpacity>
      ) : (
        <View style={styles.iconButton} />
      )}

      <View style={styles.headerCenter}>
        <Text style={[styles.headerTitle, isDark ? styles.headerTitleDark : null, titleStyle]}>{title}</Text>
        {!!subtitle && <Text style={[styles.headerSubtitle, isDark ? styles.headerSubtitleDark : null]}>{subtitle}</Text>}
      </View>

      {showRightButton ? (
        <TouchableOpacity
          activeOpacity={0.75}
          style={[styles.iconButton, isDark ? styles.iconButtonDark : null]}
          onPress={onRightPress}
          disabled={!onRightPress}
        >
          <Text style={[styles.headerIcon, isDark ? styles.headerIconDark : null]}>{rightIcon}</Text>
          {!!rightLabel && <Text style={[styles.searchLabel, isDark ? styles.searchLabelDark : null]}>{rightLabel}</Text>}
        </TouchableOpacity>
      ) : (
        <View style={styles.iconButton} />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    marginTop: 2,
    paddingHorizontal: 16,
    paddingVertical: 6,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    zIndex: 12,
  },
  headerDark: {
    marginBottom: 2,
  },
  iconButton: {
    width: 42,
    height: 42,
    justifyContent: 'center',
    alignItems: 'center',
  },
  iconButtonDark: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.18)',
    backgroundColor: 'rgba(255,255,255,0.08)',
  },
  headerIcon: {
    fontSize: 22,
    color: '#1D2935',
    textAlign: 'center',
  },
  headerIconDark: {
    color: '#def6ff',
  },
  headerCenter: {
    alignItems: 'center',
    flex: 1,
  },
  headerTitle: {
    color: '#1F8FD7',
    fontSize: 22,
    fontWeight: '800',
    writingDirection: 'rtl',
    textAlign: 'center',
  },
  headerTitleDark: {
    color: '#ffffff',
    fontWeight: '800',
    textShadowColor: 'rgba(0, 0, 0, 0.3)',
    textShadowOffset: { width: 0, height: 1 },
    textShadowRadius: 4,
  },
  headerSubtitle: {
    marginTop: 2,
    color: '#7D95AA',
    fontSize: 11,
    writingDirection: 'rtl',
    textAlign: 'center',
  },
  headerSubtitleDark: {
    color: 'rgba(255,255,255,0.72)',
  },
  searchLabel: {
    color: '#517796',
    fontSize: 11,
    marginTop: 1,
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  searchLabelDark: {
    color: 'rgba(255,255,255,0.68)',
  },
  leftLabel: {
    color: '#517796',
    fontSize: 10,
    marginTop: 1,
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  leftLabelDark: {
    color: 'rgba(255,255,255,0.68)',
  },
});
